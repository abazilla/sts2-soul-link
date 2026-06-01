using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SoulLinkMod.Patches;

/// <summary>
/// Makes Self-Forming Clay trigger for a player when their teammate takes damage,
/// if the SharedLoseHp setting is enabled.
///
/// Self-Forming Clay has no "used once per combat" guard — it stacks block every
/// time the owner takes unblocked damage. This patch mirrors that: every time a
/// teammate takes unblocked damage, the relic owner also gains the block buff.
///
/// See CentennialPuzzleSoulLinkPatch for a fuller explanation of the async
/// Postfix pattern used here.
/// </summary>
[HarmonyPatch(typeof(SelfFormingClay))]
public static class SelfFormingClaySoulLinkPatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(SelfFormingClay), nameof(SelfFormingClay.AfterDamageReceived));

    [HarmonyPostfix]
    static async Task Postfix(Task __result, SelfFormingClay __instance,
        PlayerChoiceContext choiceContext, Creature target, DamageResult result,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        await __result;

        if (!SoulLinkSession.IsActive) return;
        if (!SoulLinkSession.ActiveRunSettings.SharedLoseHp) return;
        if (!CombatManager.Instance.IsInProgress) return;
        if (result.UnblockedDamage <= 0) return;
        if (!target.IsPlayer) return;

        var ownerCreature = __instance.Owner?.Creature;
        if (ownerCreature == null || ownerCreature == target) return;

        // Check the target is a teammate of the relic's owner.
        if (ownerCreature.CombatState == null) return;
        if (!ownerCreature.CombatState.GetTeammatesOf(target).Contains(ownerCreature)) return;

        // Mirror the original effect: apply block-next-turn power to the relic's owner.
        __instance.Flash();
        await PowerCmd.Apply<SelfFormingClayPower>(
            new ThrowingPlayerChoiceContext(),
            ownerCreature,
            __instance.DynamicVars["BlockNextTurn"].BaseValue,
            ownerCreature,
            null);
    }
}
