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
///   Remote player: redirect stale STS2-sync write back to canonical so the backing
///   field is never clobbered.
///
/// SplitByPlayer — each player has their own pool.  Local player gains are divided
///   by playerCount; spends are full.  Mirror keeps other player objects showing their
///   own _playerGold[] values.  Broadcast carries this player's new canonical.
///   Remote player: redirect to that player's _playerGold[] value.
///
/// Note: ShouldBroadcast=false on SoulLinkGoldSyncMessage so the host does NOT
/// process its own message (preventing double-logging with scaled deltas).
/// </summary>
[HarmonyPatch(typeof(Player))]
public static class GoldSyncPatch
{
    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Player), nameof(Player.Gold));

    static void Prefix(Player __instance, ref int value)
    {
        if (SoulLinkMod.ApplyingCanonical) return;
        if (!SoulLinkSession.IsActive) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        // Find this player's slot. Skip entirely if they're not in the current run.
        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i] == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return;

        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;

        // ── Default mode ────────────────────────────────────────────────────────
        // STS2 native gold — Soul Link doesn't intercept anything.
        if (goldMode == GoldSharingMode.Default) return;

        // ── Remote player ───────────────────────────────────────────────────────
        // STS2's periodic sync may carry a stale value. Redirect to canonical
        // so the backing field is never clobbered.
        if (!LocalContext.IsMe(__instance))
        {
            value = goldMode == GoldSharingMode.SplitByPlayer
                ? SoulLinkSession.GetPlayerGold(playerSlot)
                : SoulLinkSession.Gold;
            return;
        }

        // ── Local player ────────────────────────────────────────────────────────
        int delta = value - __instance.Gold;
        if (delta == 0) return;

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
    }
}
