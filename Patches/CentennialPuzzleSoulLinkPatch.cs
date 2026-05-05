using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SoulLinkMod.Patches;

/// <summary>
/// Makes Centennial Puzzle trigger for a player when their teammate takes damage,
/// if the SharedLoseHp setting is enabled.
///
/// How it works (ELI5 version):
/// The game already calls every relic's AfterDamageReceived when anyone takes damage.
/// So PlayerA's CentennialPuzzle gets called with "PlayerB took damage" — it just
/// immediately returns because of the guard "target == my owner". This async Postfix
/// runs AFTER that early return and applies the draw-3 effect to the relic's owner
/// if the target was a living teammate.
///
/// Harmony 2.x supports async Task Postfixes: declare the method as async Task,
/// take Task __result as first parameter, then await __result to wait for the
/// original to finish before running our additional logic.
/// </summary>
[HarmonyPatch(typeof(CentennialPuzzle))]
public static class CentennialPuzzleSoulLinkPatch
{
    private static readonly FieldInfo _usedThisCombatField =
        AccessTools.Field(typeof(CentennialPuzzle), "_usedThisCombat");

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(CentennialPuzzle), nameof(CentennialPuzzle.AfterDamageReceived));

    [HarmonyPostfix]
    static async Task Postfix(Task __result, CentennialPuzzle __instance,
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

        // Guard: only trigger once per combat (same as the original logic).
        bool alreadyUsed = (bool)_usedThisCombatField.GetValue(__instance);
        if (alreadyUsed) return;

        // Mirror the original effect: flash, mark used, draw 3 cards for the relic's owner.
        // IMPORTANT: we must create a choice context owned by the *relic holder*, not the damage
        // target.  CardPileCmd.Draw routes the draw action through the context owner's action
        // queue; using the target's context here would enqueue the draw under the wrong player,
        // causing a host/client state divergence (SL-004).
        var ownerContext = new HookPlayerChoiceContext(
            __instance.Owner,
            LocalContext.NetId.Value,
            GameActionType.Combat);
        __instance.Flash();
        _usedThisCombatField.SetValue(__instance, true);
        await CardPileCmd.Draw(ownerContext, __instance.DynamicVars.Cards.BaseValue, __instance.Owner);
    }
}
