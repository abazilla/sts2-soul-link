using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Potions;

namespace SoulLinkMod.Patches;

/// <summary>
/// Makes Fairy in a Bottle save a teammate from death, if SharedLoseHp is enabled.
///
/// Fairy in a Bottle uses ShouldDie (not ShouldDieLate), so it has priority over
/// Lizard Tail. If both are held by different players, Fairy in a Bottle fires first.
///
/// AfterPreventingDeath on FairyInABottle calls OnUseWrapper which heals the dying
/// creature for 30% MaxHp and consumes the potion. In Soul Link, that heal
/// propagates to all players via HealInternalPatch.
/// </summary>
[HarmonyPatch(typeof(FairyInABottle), nameof(FairyInABottle.ShouldDie))]
public static class FairyInABottleSoulLinkPatch
{
    static void Postfix(ref bool __result, FairyInABottle __instance, Creature creature)
    {
        if (!__result) return; // Original already saving someone — nothing to do.

        if (!SoulLinkSession.IsActive) return;
        if (!SoulLinkSession.ActiveRunSettings.SharedLoseHp) return;

        var owner = __instance.Owner?.Creature;
        if (owner == null || owner == creature) return;
        if (owner.CombatState == null) return;
        if (!owner.CombatState.GetTeammatesOf(creature).Contains(owner)) return;

        __result = false; // Prevent the teammate's death.
    }
}
