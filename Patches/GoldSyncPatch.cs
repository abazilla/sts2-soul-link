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
/// Patches Player.Gold setter to manage the shared gold pool for all players.
///
/// Two paths:
///
/// LOCAL PLAYER — a legitimate game event changed this player's gold (combat reward,
///   purchase, Neow bonus, etc.).  Calculate the delta, apply it to the canonical pool,
///   redirect the setter value to canonical, mirror it to all other player objects on
///   this machine, and broadcast the canonical to the other machine.
///
/// REMOTE PLAYER — STS2's own periodic state-sync is writing the remote player's gold
///   from their home machine.  That packet may carry a stale value (e.g. Machine A still
///   shows 99 gold because our SoulLinkGoldSyncMessage hasn't arrived yet).  We redirect
///   the value to canonical so the backing field is never clobbered with stale data.
///   This is what keeps the backing field correct on both machines, which is what STS2's
///   ChecksumTracker reads when it generates the per-room checksum.
///
/// Note: we do NOT patch the getter.  Patching the getter made STS2's setter body see
///   old == new (because canonical was updated in our prefix before the setter ran), so
///   STS2 suppressed its GoldChanged event and the UI gold display stopped updating.
///   Blocking stale remote writes through the setter is sufficient and avoids that issue.
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

        // ── Remote player ───────────────────────────────────────────────────────
        // STS2's periodic sync is writing this player's gold from their home machine.
        // The packet may be stale (sent before our GoldSyncMessage arrived there),
        // so redirect to canonical to keep the backing field consistent for checksums.
        if (!LocalContext.IsMe(__instance))
        {
            value = SoulLinkSession.Gold;
            return;
        }

        // ── Local player ────────────────────────────────────────────────────────
        int delta = value - __instance.Gold;
        if (delta == 0) return;

        // Check for Ectoplasm or STS2 equivalent gold-blocking relic.
        // TODO: verify the STS2 relic ID for the Ectoplasm equivalent.
        bool blocked = delta > 0 && __instance.Relics.Any(r =>
            r.Id?.Entry == "Ectoplasm");

        int canonical = SoulLinkSession.ApplyGoldDelta(delta, playerSlot, blocked,
            blockSource: blocked ? "Ectoplasm" : null);

        // Redirect the local player's gold to the canonical total.
        value = canonical;

        // Mirror canonical to all other players on this machine immediately —
        // don't wait for STS2's next sync cycle (which may arrive after the checksum).
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                if (player != __instance)
                    player.Gold = canonical;
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Broadcast the canonical gold to the other machine.
        RunManager.Instance?.NetService?.SendMessage(
            new SoulLinkGoldSyncMessage { CanonicalGold = canonical });

        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
