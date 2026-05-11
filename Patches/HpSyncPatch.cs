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
    internal static int DebugGetQueueSize() => 0;

    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp));

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

        int delta = value - __instance.CurrentHp;
        if (delta == 0) return;

        bool isLocalPlayer = LocalContext.IsMe(runState.Players[playerSlot]);
        Godot.GD.Print($"[SoulLink][HpSync] Prefix: slot={playerSlot} delta={delta} isLocal={isLocalPlayer} poolBefore={SoulLinkSession.CurrentHp}");

        bool inCombat = CombatManager.Instance.IsInProgress;
        int playerCount = runState.Players.Count;

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource
            ?? (inCombat ? "Combat" : "Out of combat");
        SoulLinkSession.PendingSource = null;

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup)
        int? result = SyncCoordinator.TryApplyHpDelta(playerSlot, delta, isFromNetwork: false, inCombat, playerCount, source);
        if (result == null)
        {
            // Already applied by network path - redirect to canonical to prevent stale write
            value = SyncCoordinator.GetCanonicalHp();
            return;
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

        // VGQ path: Use vanilla GameAction queue for synchronization
        // NOTE: VGQ implementation currently blocked on type resolution (see VGQ/SoulLinkHpChangeGameAction.cs)
        // TODO: Uncomment when SoulLinkHpChangeGameAction and ActionQueueSynchronizer are functional
        // if (FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)
        //     && SoulLinkSession.IsInitPhaseComplete
        //     && !inCombat)
        // {
        //     if (isLocalPlayer)
        //     {
        //         // Enqueue HP change to vanilla action queue (VGQ architecture)
        //         ActionQueueSynchronizer.RequestEnqueueHpChange(delta, playerSlot, inCombat, source);
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
    }
}
