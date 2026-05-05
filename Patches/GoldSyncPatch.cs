using System.Linq;
using System.Reflection;
using HarmonyLib;
using SoulLinkMod.UI;
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
///   Remote player: setter blocked entirely (return false). Gold is only written via
///   GoldSyncHandler under ApplyingCanonical=true. GoldSyncHandler uses delta-based
///   accumulation (not absolute overwrite) so concurrent independent gold events on
///   both machines converge correctly without a race condition.
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

        // ── Remote player ───────────────────────────────────────────────────────
        // Block ALL game-initiated gold writes for remote players. Gold is only
        // written via GoldSyncHandler under ApplyingCanonical=true.
        // In SharedPool this prevents a race condition: GoldSyncHandler uses
        // delta-based accumulation so both machines correctly converge even when
        // each only sees their own player's local event.
        // In SplitByPlayer this prevents double-applying the scaled delta that
        // GoldSyncHandler distributes to all other player slots.
        if (!LocalContext.IsMe(__instance))
            return false;

        // ── Local player ────────────────────────────────────────────────────────
        int delta = value - __instance.Gold;
        if (delta == 0) return true;

        int playerCount = runState.Players.Count;

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

        // Broadcast the canonical gold and the actual scaled delta.
        RunManager.Instance?.NetService?.SendMessage(new SoulLinkGoldSyncMessage
        {
            CanonicalGold = canonical,
            Delta         = broadcastDelta,
            PlayerSlot    = playerSlot,
        });

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
        return true;
    }
}
