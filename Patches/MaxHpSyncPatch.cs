using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.UI;
using SoulLinkMod.VGQ;

using SoulLinkMod;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.MaxHp setter to intercept max-HP changes on player creatures.
///
/// Out-of-combat max-HP gains are divided by player count (same scaling rule as heals).
/// Writes the canonical MaxHp (and possibly-clamped CurrentHp) back to all players
/// via CreatureHelper reflection, since the setters are private.
///
/// Dedup dictionaries and lock are owned by SyncCoordinator.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class MaxHpSyncPatch
{
    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Creature), nameof(Creature.MaxHp));

    static bool Prefix(Creature __instance, ref int value)
    {
        if (SoulLinkMod.ApplyingCanonical) return true;
        if (!SoulLinkSession.IsActive) return true;
        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return true;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return true;

        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return true;

        int delta = value - __instance.MaxHp;
        if (delta == 0) return true;

        bool isLocalPlayer = LocalContext.IsMe(runState.Players[playerSlot]);
        SoulLinkLog.Debug($"[MaxHpSync] Prefix: slot={playerSlot} delta={delta} isLocal={isLocalPlayer} poolBefore={SoulLinkSession.MaxHp}");

        bool inCombat = SoulLinkMod.IsCombatActive();
        int playerCount = runState.Players.Count;

        // No init-phase MaxHp branch: vanilla character/ascension setup never writes MaxHp
        // through the public setter (it uses reflection on the backing field), so any
        // MaxHp setter call we intercept during init is already a real player-visible
        // event (Neow downside, etc.) that must go through ApplyMaxHpDelta for proper
        // SplitMaxHp scaling and delta-correct logging. Falling into the canonical-pool
        // path also avoids the bug where comparing `value` against the (lazy-grown)
        // canonical pool produced wrong deltas while creature MaxHp was higher than pool.

        if (inCombat && !SoulLinkSession.IsInitPhaseComplete)
            SoulLinkSession.MarkInitPhaseComplete();

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource
            ?? (inCombat ? "Combat" : "Out of combat");
        SoulLinkSession.PendingSource = null;

        // VGQ path owns the apply on every peer via ExecuteAction. The patch only
        // detects + enqueues on the originating peer. Skip local mutation entirely
        // to avoid double-apply when VGQ executes.
        bool useVgqBroadcast = FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)
            && SoulLinkSession.IsInitPhaseComplete
            && !inCombat;
        if (useVgqBroadcast)
        {
            if (isLocalPlayer)
            {
                ActionQueueSynchronizer.RequestEnqueueMaxHpChange(delta, playerSlot, inCombat, playerCount, source);
            }
            // Let vanilla setter pass through. `runManager.ActionQueueSynchronizer.RequestEnqueue`
            // is asynchronous (vanilla GameAction queue dispatches the action later), so blocking
            // the setter here would leave creature.MaxHp stale and break callers that read it back
            // synchronously — notably CreatureCmd.SetMaxHp -> `newMaxHp - oldMaxHp` -> Heal(num).
            // With num always 0, every GainMaxHp caller (Pear, Mango, Strawberry, Feed, Cook, etc.)
            // silently lost its bundled heal.
            //
            // Passing through writes the raw new value to this creature; the subsequent
            // ExecuteAction will overwrite all creatures with the canonical pool value
            // (idempotent for shared pool; brief skew under SplitMaxHp until apply).
            //
            // Side effect — LeesWaffle double-heals: it calls GainMaxHp (now heals via
            // pass-through) then explicitly `Heal(creature, MaxHp - CurrentHp)`. The
            // second heal reads stale creature.CurrentHp (HpSyncPatch blocks the local
            // write) and heals the full gap again. Net effect: still heal-to-full, since
            // pool clamps at MaxHp. No visible difference. Acceptable.
            return true;
        }

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup)
        bool applied = SyncCoordinator.TryApplyMaxHpDelta(playerSlot, delta, isFromNetwork: false, inCombat, playerCount, source);
        if (!applied)
        {
            // Already applied by network path - redirect to canonical to prevent stale write
            value = SyncCoordinator.GetCanonicalMaxHp();
            return true;
        }

        // Redirect the write on the triggering player.
        value = SoulLinkSession.MaxHp;

        // Write canonical MaxHp and CurrentHp to all other players.
        // The triggering creature gets MaxHp via the redirected `value` above.
        // Its CurrentHp will be updated by the follow-up CurrentHp write the game fires
        // immediately after (the heal equal to the MaxHp gain), intercepted by HpSyncPatch.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                if (player.Creature != __instance)
                {
                    player.Creature.SetMaxHp(SoulLinkSession.MaxHp);
                    player.Creature.SetCurrentHp(SoulLinkSession.CurrentHp);
                }
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // MNA path: Use Mod Net Action pipeline (transitional, to be deprecated)
        if (FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions)
            && SoulLinkSession.IsInitPhaseComplete
            && !inCombat)
        {
            if (isLocalPlayer)
            {
                // Local player action - broadcast to peers (out-of-combat only)
                NetActionService.EnqueueLocalAction(new MaxHpChangeAction
                {
                    DeltaMaxHp = delta,
                    PlayerSlot = playerSlot,
                    InCombat = inCombat,
                    Source = source,
                }, playerSlot);
            }
        }

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
        return true;
    }
}
