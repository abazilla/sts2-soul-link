using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.UI;
using SoulLinkMod.VGQ;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.CurrentHp setter to intercept all HP changes on player creatures.
///
/// When a player's HP is written:
///  1. Calculate the delta.
///  2. Pass it to SyncCoordinator to atomically check-apply-mark (thread-safe dedup).
///  3. Overwrite `value` with the canonical shared HP so the triggering player lands on it.
///  4. Write the canonical value back to every other player via CreatureHelper (reflection).
///
/// The ApplyingCanonical guard prevents re-entrancy when writing back.
/// Dedup dictionaries and lock are owned by SyncCoordinator.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class HpSyncPatch
{
    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp));

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

        int delta = value - __instance.CurrentHp;
        if (delta == 0) return true;

        bool isLocalPlayer = LocalContext.IsMe(runState.Players[playerSlot]);
        Godot.GD.Print($"[SoulLink][HpSync] Prefix: slot={playerSlot} delta={delta} isLocal={isLocalPlayer} poolBefore={SoulLinkSession.CurrentHp}");

        bool inCombat = SoulLinkMod.IsCombatActive();
        int playerCount = runState.Players.Count;

        // Init-phase overwrite path. Until the first combat starts, vanilla performs
        // per-peer initialization writes (character setup, ascension scaling, Neow setup)
        // whose intermediate values vary and whose ordering is opaque. Trusting the
        // final written value avoids the N× delta drain that previously zeroed the pool.
        // Legitimate Neow damage options (Precarious Shears, Lament) flow through the
        // same path: each writes a final HP value that overwrites canonical, then
        // mirror propagates it to all peers.
        if (!SoulLinkSession.IsInitPhaseComplete && !inCombat)
        {
            SoulLinkSession.OverwriteCanonicalHp(value);
            int canonical = SoulLinkSession.CurrentHp;
            value = canonical;
            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                foreach (var player in runState.Players)
                    if (player.Creature != __instance)
                        player.Creature.SetCurrentHp(canonical);
            }
            finally { SoulLinkMod.ApplyingCanonical = false; }
            CombatLogPanel.Current?.Refresh();
            RunStatsPanel.Current?.Refresh();
            DebugOverlay.Current?.Refresh();
            return true;
        }

        // First combat write: leave init phase.
        if (inCombat && !SoulLinkSession.IsInitPhaseComplete)
            SoulLinkSession.MarkInitPhaseComplete();

        string? source = SoulLinkSession.PendingSource
            ?? ResolveActiveAttacker(__instance, inCombat)
            ?? SoulLinkSession.CurrentRoomSource
            ?? (inCombat ? "Combat" : "Out of combat");
        SoulLinkSession.PendingSource = null;

        // VGQ path owns the apply on every peer via ExecuteAction. Patch only
        // detects + enqueues on the originating peer. Skip local mutation entirely
        // to avoid double-apply when VGQ executes.
        bool useVgqBroadcast = FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)
            && SoulLinkSession.IsInitPhaseComplete
            && !inCombat;
        if (useVgqBroadcast)
        {
            if (isLocalPlayer)
            {
                ActionQueueSynchronizer.RequestEnqueueHpChange(delta, playerSlot, inCombat, source);
            }
            // Block vanilla setter on every peer. VGQ ExecuteAction runs synchronously
            // inside RequestEnqueue on the originator and has already mirrored the
            // canonical to all creatures (including this one) under ApplyingCanonical.
            // Letting vanilla resume would overwrite the canonical with the raw value.
            return false;
        }

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup)
        int? result = SyncCoordinator.TryApplyHpDelta(playerSlot, delta, isFromNetwork: false, inCombat, playerCount, source);
        if (result == null)
        {
            // Already applied by network path - redirect to canonical to prevent stale write
            value = SyncCoordinator.GetCanonicalHp();
            return true;
        }

        // Redirect the write on the triggering player.
        value = result.Value;

        // Write canonical to all other players.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                if (player.Creature != __instance)
                    player.Creature.SetCurrentHp(result.Value);
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Broadcast the HP change.
        // Skip during init phase (Neow) and combat - both are deterministic.
        // Combat: game syncs card plays, each client processes independently.
        // Broadcasting during combat causes race with relics like Rupture.

        // MNA path: Use Mod Net Action pipeline (transitional, to be deprecated)
        if (FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions)
            && SoulLinkSession.IsInitPhaseComplete
            && !inCombat)
        {
            if (isLocalPlayer)
            {
                // Local player action - broadcast to peers (out-of-combat only)
                NetActionService.EnqueueLocalAction(new HpChangeAction
                {
                    DeltaHp = delta,
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

    // Find the monster currently mid-move so combat damage to a player can be
    // labelled with the actual attacker (e.g. "Byrdonis") instead of "Combat".
    // Relies on MonsterModel.IsPerformingMove being true for exactly the attacker
    // for the duration of its move; falls back to null if nothing matches.
    private static string? ResolveActiveAttacker(Creature playerCreature, bool inCombat)
    {
        if (!inCombat) return null;
        var cs = playerCreature.CombatState;
        if (cs == null) return null;
        foreach (var enemy in cs.Enemies)
        {
            var m = enemy.Monster;
            if (m != null && m.IsPerformingMove)
                return enemy.Name;
        }
        return null;
    }
}
