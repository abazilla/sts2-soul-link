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
/// Patches Player.Gold setter to intercept all gold changes.
///
/// Only fires for the local player — remote players' gold is written by STS2's own
/// multiplayer sync and should not double-trigger our pool update.
///
/// Checks for Ectoplasm (or STS2 equivalent): if the player has a gold-blocking relic
/// and delta > 0, the gain is logged as blocked and NOT applied to the pool.
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

        // Only intercept for the local player.
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;
        if (!LocalContext.IsMe(__instance)) return;

        int delta = value - __instance.Gold;
        if (delta == 0) return;

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

        // Check for Ectoplasm or STS2 equivalent gold-blocking relic.
        // TODO: verify the STS2 relic ID for the Ectoplasm equivalent.
        bool blocked = delta > 0 && __instance.Relics.Any(r =>
            r.Id?.Entry == "Ectoplasm");

        int canonical = SoulLinkSession.ApplyGoldDelta(delta, playerSlot, blocked,
            blockSource: blocked ? "Ectoplasm" : null);

        // Redirect to the canonical gold total for all players.
        value = canonical;

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

        // Broadcast the canonical gold to the other process via STS2's message system.
        // SoulLinkGoldSyncMessage is auto-registered because STS2 scans mod assemblies
        // for INetMessage implementations (ReflectionHelper.GetSubtypesInMods).
        RunManager.Instance?.NetService?.SendMessage(
            new SoulLinkGoldSyncMessage { CanonicalGold = canonical });

        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
