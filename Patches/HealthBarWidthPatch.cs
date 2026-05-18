using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.Patches;

/// <summary>
/// When StartingHpMode is Additive, divides the multiplayer player HP bar visual width
/// by the number of players in the run, so the bar's length matches a single-player bar.
/// </summary>
[HarmonyPatch(typeof(NHealthBar), nameof(NHealthBar.UpdateWidthRelativeToReferenceValue))]
public static class HealthBarWidthPatch
{
    static void Prefix(NHealthBar __instance, ref float refMaxHp)
    {
        if (!SoulLinkSession.IsActive) return;
        var rs = SoulLinkSession.ActiveRunSettings;
        if (rs.HpMode == HpMode.Vanilla) return;
        if (rs.StartingHpMode != StartingHpMode.Additive) return;

        var creatureField = AccessTools.Field(typeof(NHealthBar), "_creature");
        if (creatureField == null) return;
        var creature = creatureField.GetValue(__instance) as Creature;
        if (creature == null) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        bool isPlayer = false;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == creature) { isPlayer = true; break; }
        }
        if (!isPlayer) return;

        int n = runState.Players.Count;
        if (n <= 1) return;

        refMaxHp *= n;
    }
}
