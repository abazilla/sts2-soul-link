using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SoulLinkMod.Patches;

/// <summary>
/// Makes Lizard Tail save a teammate from death, if SharedLoseHp is enabled.
///
/// How it works:
/// ShouldDieLate returns false (= "don't let them die yet") for the relic owner.
/// This Postfix also returns false when the dying creature is a living teammate.
///
/// After this, the game calls AfterPreventingDeath ONLY on the preventer (Lizard Tail),
/// which heals the dying creature for 50% MaxHp. In Soul Link, that heal propagates
/// to all players via HealInternalPatch.
///
/// Note: WasUsed is set inside AfterPreventingDeath automatically — no extra bookkeeping
/// needed here. The relic is consumed after one use regardless of who it saves.
/// </summary>
[HarmonyPatch(typeof(LizardTail), nameof(LizardTail.ShouldDieLate))]
public static class LizardTailSoulLinkPatch
{
    static void Postfix(ref bool __result, LizardTail __instance, Creature creature)
    {
        if (!__result) return; // Original already saving someone — nothing to do.

        if (!SoulLinkSession.IsActive) return;
        if (!SoulLinkSession.ActiveRunSettings.SharedLoseHp) return;
        if (__instance.WasUsed) return;

        var owner = __instance.Owner?.Creature;
        if (owner == null || owner == creature) return;
        if (owner.CombatState == null) return;
        if (!owner.CombatState.GetTeammatesOf(creature).Contains(owner)) return;

        __result = false; // Prevent the teammate's death.
    }
}
