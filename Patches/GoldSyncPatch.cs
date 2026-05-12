using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using SoulLinkMod.Actions;
using SoulLinkMod.UI;
using SoulLinkMod.VGQ;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Player.Gold setter to manage gold per the active GoldSharingMode.
///
/// THREE MODES:
///
/// Default — returns early; STS2 manages gold natively, Soul Link doesn't intercept.
///
/// SharedPool — one canonical pool.  Local player: compute delta, update pool, mirror
///   canonical to all other player objects on this machine, broadcast.
///   Remote player: in-band delta apply for deterministic changes (enemy steal, event
///   replay) with two-direction dedup to prevent double-apply regardless of ordering:
///     _pendingCancellations — setter fires first → GoldSyncHandler consumes on arrival.
///     _networkApplied       — GoldSyncHandler fires first → setter consumes when fired.
///
/// SplitByPlayer — each player has their own pool.  Local player gains are divided
///   by playerCount; spends are full.  Mirror keeps other player objects showing their
///   own _playerGold[] values.  Broadcast carries this player's new canonical.
///   Remote player: setter blocked entirely. GoldSyncHandler on the receiver applies
///   the scaled delta to all other slots; allowing the remote setter through would
///   double-apply that delta.
///
/// Note: ShouldBroadcast=false on SoulLinkGoldSyncMessage so the host does NOT
/// process its own message (preventing double-logging with scaled deltas).
/// </summary>
[HarmonyPatch(typeof(Player))]
public static class GoldSyncPatch
{
    // Bidirectional dedup for gold changes. Handles both orderings:
    //   1. Local fires first → message arrives: _pendingCancellations
    //   2. Message arrives first → local fires: _networkApplied

    // Direction 1: Local event fires before network message arrives
    private static readonly Dictionary<(int slot, int delta), int> _pendingCancellations = new();

    // Direction 2: Network message arrives before local event fires
    // Uses slot-only matching because GoldSyncHandler updates player.Gold before the
    // setter fires, so the computed delta may differ from the original message delta.
    private static readonly Dictionary<int, int> _networkApplied = new();

    internal static bool TryConsumeCancellation(int slot, int delta)
    {
        var key = (slot, delta);
        if (!_pendingCancellations.TryGetValue(key, out var count) || count == 0)
            return false;
        if (count == 1)
            _pendingCancellations.Remove(key);
        else
            _pendingCancellations[key] = count - 1;
        return true;
    }

    internal static void EnqueueCancellation(int slot, int delta)
    {
        var key = (slot, delta);
        _pendingCancellations.TryGetValue(key, out var count);
        _pendingCancellations[key] = count + 1;
    }

    internal static bool TryConsumeNetworkApplied(int slot)
    {
        if (!_networkApplied.TryGetValue(slot, out var count) || count == 0)
            return false;
        if (count == 1)
            _networkApplied.Remove(slot);
        else
            _networkApplied[slot] = count - 1;
        return true;
    }

    internal static void EnqueueNetworkApplied(int slot)
    {
        _networkApplied.TryGetValue(slot, out var count);
        _networkApplied[slot] = count + 1;
    }

    internal static void ClearCancellations()
    {
        _pendingCancellations.Clear();
        _networkApplied.Clear();
    }

    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Player), nameof(Player.Gold));

    // Returns false to block the setter for all remote players in non-Default modes.
    // Returns true to let the setter run for local players and canonical writes.
    static bool Prefix(Player __instance, ref int value)
    {
        // Canonical writes (from GoldSyncHandler / mirror logic) always go through.
        if (SoulLinkMod.ApplyingCanonical) return true;
        if (!SoulLinkSession.IsActive) return true;
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return true;

        // Find this player's slot. If not in the current run, let STS2 handle it.
        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i] == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return true;

        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;

        // ── Default mode ────────────────────────────────────────────────────────
        // STS2 native gold — Soul Link doesn't intercept anything.
        if (goldMode == GoldSharingMode.Default) return true;

        int playerCount = runState.Players.Count;

        // VGQ path owns the apply on every peer via ExecuteAction. The patch only
        // detects the delta on the originating (local-player) peer and enqueues.
        // For non-local-player setters here, do nothing — VGQ will broadcast to us.
        bool useVgqBroadcast = FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)
            && SoulLinkSession.IsInitPhaseComplete;
        if (useVgqBroadcast)
        {
            if (LocalContext.IsMe(__instance))
            {
                int vgqDelta = value - __instance.Gold;
                if (vgqDelta != 0)
                {
                    string? vgqSource = SoulLinkSession.PendingSource
                        ?? SoulLinkSession.CurrentRoomSource;
                    SoulLinkSession.PendingSource = null;
                    bool inCombat = SoulLinkMod.IsCombatActive();
                    ActionQueueSynchronizer.RequestEnqueueGoldChange(vgqDelta, playerSlot, goldMode, inCombat, vgqSource);
                }
            }
            // Block vanilla setter on every peer. VGQ ExecuteAction runs synchronously
            // inside RequestEnqueue on the originator and has already mirrored the
            // canonical to all players (including this one) under ApplyingCanonical.
            // Letting vanilla resume would overwrite the canonical with the raw value.
            return false;
        }

        // ── Remote player ───────────────────────────────────────────────────────
        if (!LocalContext.IsMe(__instance))
        {
            // SharedPool: enemy-initiated gold changes (e.g. Gremlin Merc steal) are
            // deterministic — STS2 fires the setter on BOTH machines for the same player.
            // Apply the delta locally in-band so the canonical pool is correct before the
            // checksum runs, without waiting for a network round-trip.  We enqueue the
            // (slot, delta) so GoldSyncHandler can cancel the redundant broadcast when
            // it arrives (preventing a double-apply).
            //
            // SplitByPlayer: keep blocking; the peer broadcasts the canonical value
            // and GoldSyncHandler distributes the scaled delta.
            if (goldMode == GoldSharingMode.SharedPool)
            {
                int remDelta = value - __instance.Gold;
                GD.Print($"[SoulLink][GoldSync] Remote setter slot={playerSlot} curGold={__instance.Gold} newValue={value} remDelta={remDelta} canonicalGold={SoulLinkSession.Gold} networkAppliedCount={_networkApplied.Count}");
                if (remDelta == 0) return false;

                // GoldSyncHandler already applied this purchase from the network broadcast
                // before the game's GoldLostMessage fired the setter.  The canonical is
                // already correct — redirect to it so the game's setter writes the right
                // value without double-applying the delta.
                bool consumed = TryConsumeNetworkApplied(playerSlot);
                GD.Print($"[SoulLink][GoldSync] TryConsumeNetworkApplied({playerSlot})={consumed}");
                if (consumed)
                {
                    // Redirect the setter value to the already-applied canonical so all
                    // player objects stay in sync. (GoldSyncHandler updated the session and
                    // mirrored to other players; letting this setter run with the canonical
                    // value updates this player object to match.)
                    value = SoulLinkSession.Gold;
                    SoulLinkMod.ApplyingCanonical = true;
                    try
                    {
                        for (int i = 0; i < runState.Players.Count; i++)
                        {
                            if (runState.Players[i] == __instance) continue;
                            runState.Players[i].Gold = value;
                        }
                    }
                    finally
                    {
                        SoulLinkMod.ApplyingCanonical = false;
                    }
                    return true;
                }

                bool remBlocked = remDelta > 0 && __instance.Relics.Any(r =>
                    r.Id?.Entry == "Ectoplasm");

                string? remSource = SoulLinkSession.PendingSource
                    ?? SoulLinkSession.CurrentRoomSource;
                SoulLinkSession.PendingSource = null;

                int remCanonical = SoulLinkSession.ApplyGoldDelta(remDelta, playerCount, playerSlot, remBlocked,
                    blockSource: remBlocked ? "Ectoplasm" : null,
                    source: remBlocked ? null : remSource);
                value = remCanonical;

                SoulLinkMod.ApplyingCanonical = true;
                try
                {
                    for (int i = 0; i < runState.Players.Count; i++)
                    {
                        if (runState.Players[i] == __instance) continue;
                        runState.Players[i].Gold = remCanonical;
                    }
                }
                finally
                {
                    SoulLinkMod.ApplyingCanonical = false;
                }

                // Enqueue for cancellation so GoldSyncHandler ignores the peer's broadcast.
                if (!remBlocked)
                    EnqueueCancellation(playerSlot, remDelta);

                CombatLogPanel.Current?.Refresh();
                RunStatsPanel.Current?.Refresh();
                DebugOverlay.Current?.Refresh();
                return true;
            }
            return false;
        }

        // ── Local player ────────────────────────────────────────────────────────
        int delta = value - __instance.Gold;
        if (delta == 0) return true;

        // Check for Ectoplasm or STS2 equivalent gold-blocking relic.
        bool blocked = delta > 0 && __instance.Relics.Any(r =>
            r.Id?.Entry == "Ectoplasm");

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource;
        SoulLinkSession.PendingSource = null;

        // Capture pre-call canonical so we can compute the actual applied delta.
        int prevCanonical = goldMode == GoldSharingMode.SplitByPlayer
            ? SoulLinkSession.GetPlayerGold(playerSlot)
            : SoulLinkSession.Gold;

        int canonical = SoulLinkSession.ApplyGoldDelta(delta, playerCount, playerSlot, blocked,
            blockSource: blocked ? "Ectoplasm" : null,
            source: blocked ? null : source);
        int broadcastDelta = canonical - prevCanonical;

        // Redirect the local player's gold to the canonical value.
        value = canonical;

        // Mirror canonical to all other players on this machine immediately.
        // In SharedPool: everyone sees the same canonical.
        // In SplitByPlayer: each player object shows their own _playerGold[] value.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            for (int i = 0; i < runState.Players.Count; i++)
            {
                if (runState.Players[i] == __instance) continue;
                runState.Players[i].Gold = goldMode == GoldSharingMode.SplitByPlayer
                    ? SoulLinkSession.GetPlayerGold(i)
                    : canonical;
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Broadcast the gold change (legacy paths only; VGQ handled at top of method).
        // MNA path: Use Mod Net Action pipeline (transitional)
        if (FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
        {
            NetActionService.EnqueueLocalAction(new GoldChangeAction
            {
                DeltaGold = broadcastDelta,
                PlayerSlot = playerSlot,
                Source = source,
                WasBlocked = false,
            }, playerSlot);
        }
        // Legacy Wire path: Direct gold sync message (to be deprecated)
        else
        {
            RunManager.Instance?.NetService?.SendMessage(new SoulLinkGoldSyncMessage
            {
                CanonicalGold = canonical,
                Delta         = broadcastDelta,
                PlayerSlot    = playerSlot,
            });
        }

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
        return true;
    }
}
