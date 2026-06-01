using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace SoulLinkMod.Patches;

/// <summary>
/// Mirrors DamageReceivedEntry history records to teammates when SharedLoseHp is enabled.
///
/// When PlayerB takes unblocked damage, a DamageReceivedEntry is recorded for PlayerB.
/// This patch also adds an identical entry for each of PlayerB's living teammates (e.g.
/// PlayerA), so that effects which query history — EmotionChip, Spite, Tear Asunder —
/// see "PlayerA also lost HP" and trigger correctly.
///
/// No async complexity: CombatHistory.DamageReceived is synchronous.
/// No re-entrancy risk: we call the private Add() directly, not DamageReceived() again.
/// </summary>
[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.DamageReceived))]
public static class SharedLoseHpHistoryPatch
{
    private static readonly MethodInfo _addMethod =
        AccessTools.Method(typeof(CombatHistory), "Add");

    static void Postfix(CombatHistory __instance, CombatState combatState, Creature receiver,
        Creature? dealer, DamageResult result, CardModel? cardSource)
    {
        if (!SoulLinkSession.IsActive) return;
        if (!SoulLinkSession.ActiveRunSettings.SharedLoseHp) return;
        if (result.UnblockedDamage <= 0) return;
        if (!receiver.IsPlayer) return;

        foreach (Creature teammate in combatState.GetTeammatesOf(receiver))
        {
            if (teammate == receiver) continue;
            if (!teammate.IsPlayer) continue;
            if (!teammate.IsAlive) continue;

            var entry = new DamageReceivedEntry(
                result, teammate, dealer, cardSource,
                combatState.RoundNumber, combatState.CurrentSide, __instance);

            _addMethod.Invoke(__instance, new object[] { entry });
        }
    }
}
