using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.CurrentHp setter to intercept all HP changes on player creatures.
///
/// When a player's HP is written:
///  1. Calculate the delta.
///  2. Pass it to SoulLinkSession to update the shared pool (with out-of-combat scaling).
///  3. Overwrite `value` with the canonical shared HP so the triggering player lands on it.
///  4. Write the canonical value back to every other player via CreatureHelper (reflection).
///
/// The ApplyingCanonical guard prevents re-entrancy when writing back.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class HpSyncPatch
{
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

        bool inCombat = CombatManager.Instance.IsInProgress;
        int playerCount = runState.Players.Count;

        string? source = inCombat ? "Combat" : "Out of combat";
        int canonical = SoulLinkSession.ApplyHpDelta(delta, inCombat, playerCount, playerSlot, source);

        // Redirect the write on the triggering player.
        value = canonical;

        // Write canonical to all other players.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                if (player.Creature != __instance)
                    player.Creature.SetCurrentHp(canonical);
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
