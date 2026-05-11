using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.UI;

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

    static void Prefix(Creature __instance, ref int value)
    {
        if (SoulLinkMod.ApplyingCanonical) return;
        if (!SoulLinkSession.IsActive) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return;

        int delta = value - __instance.MaxHp;
        if (delta == 0) return;

        bool isLocalPlayer = LocalContext.IsMe(runState.Players[playerSlot]);
        Godot.GD.Print($"[SoulLink][MaxHpSync] Prefix: slot={playerSlot} delta={delta} isLocal={isLocalPlayer} poolBefore={SoulLinkSession.MaxHp}");

        bool inCombat = CombatManager.Instance.IsInProgress;
        int playerCount = runState.Players.Count;

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource
            ?? (inCombat ? "Combat" : "Out of combat");
        SoulLinkSession.PendingSource = null;

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup)
        bool applied = SyncCoordinator.TryApplyMaxHpDelta(playerSlot, delta, isFromNetwork: false, inCombat, playerCount, source);
        if (!applied)
        {
            // Already applied by network path - redirect to canonical to prevent stale write
            value = SyncCoordinator.GetCanonicalMaxHp();
            return;
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

        // Broadcast the MaxHP change.
        // Skip during init phase (Neow) and combat - both are deterministic.
        // Combat: game syncs card plays, each client processes independently.

        // VGQ path: Use vanilla GameAction queue (target architecture, currently blocked)
        // if (FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)
        //     && SoulLinkSession.IsInitPhaseComplete
        //     && !inCombat)
        // {
        //     if (isLocalPlayer)
        //     {
        //         // Enqueue MaxHP change to vanilla action queue (VGQ architecture)
        //         ActionQueueSynchronizer.RequestEnqueueMaxHpChange(delta, playerSlot, inCombat, source);
        //     }
        // }
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
    }
}
