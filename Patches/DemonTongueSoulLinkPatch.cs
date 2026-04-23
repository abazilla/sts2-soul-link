using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SoulLinkMod.Patches;

/// <summary>
/// Makes Demon Tongue trigger for a player when their teammate takes damage,
/// if the SharedLoseHp setting is enabled.
///
/// Demon Tongue: "The first time you take damage during your turn, heal for
/// the amount lost." In Soul Link, "you" should include your teammate.
///
/// The heal goes to the RELIC OWNER (same as vanilla), which via HealInternalPatch
/// propagates to all players in the shared-HP pool.
///
/// Guard preserved: only fires during the player's own turn
/// (CombatState.CurrentSide == Owner.Side), same as the original.
/// </summary>
[HarmonyPatch(typeof(DemonTongue))]
public static class DemonTongueSoulLinkPatch
{
    private static readonly FieldInfo _triggeredThisTurnField =
        AccessTools.Field(typeof(DemonTongue), "_triggeredThisTurn");

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(DemonTongue), nameof(DemonTongue.AfterDamageReceived));

    [HarmonyPostfix]
    static async Task Postfix(Task __result, DemonTongue __instance,
        PlayerChoiceContext choiceContext, Creature target, DamageResult result,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        await __result;

        if (!SoulLinkSession.IsActive) return;
        if (!SoulLinkSession.ActiveRunSettings.SharedLoseHp) return;
        if (result.UnblockedDamage <= 0) return;
        if (!target.IsPlayer) return;

        var ownerCreature = __instance.Owner?.Creature;
        if (ownerCreature == null || ownerCreature == target) return;

        var combatState = ownerCreature.CombatState;
        if (combatState == null) return;

        // Preserve the "only during own turn" guard from the original.
        if (combatState.CurrentSide != ownerCreature.Side) return;

        if (!combatState.GetTeammatesOf(target).Contains(ownerCreature)) return;

        bool alreadyTriggered = (bool)_triggeredThisTurnField.GetValue(__instance);
        if (alreadyTriggered) return;

        _triggeredThisTurnField.SetValue(__instance, true);
        __instance.Flash();
        // Heal the relic's owner (not the teammate) — HealInternalPatch propagates it.
        await CreatureCmd.Heal(ownerCreature, result.UnblockedDamage);
    }
}
