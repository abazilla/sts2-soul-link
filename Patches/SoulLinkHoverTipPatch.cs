using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SoulLinkMod.Patches;

/// <summary>
/// Appends a "⊞ Soul Link" hover tip to relics, cards, and potions that gain
/// extra behaviour from the SharedLoseHp setting, so players can see at a
/// glance what changed.
///
/// Visible when SharedLoseHp is enabled in the current run (or in the saved
/// settings when no run is active — e.g. while in the lobby or collection screen).
/// </summary>
public static class SoulLinkHoverTipPatch
{
    private static readonly LocString _titleStr =
        new LocString("gameplay_ui", "soullink.tip.title");

    private static readonly Dictionary<Type, string> _relicDescriptions = new()
    {
        [typeof(CentennialPuzzle)] = "Also triggers when your teammate takes unblocked damage.",
        [typeof(SelfFormingClay)]  = "Also triggers when your teammate takes unblocked damage.",
        [typeof(DemonTongue)]      = "Also heals when your teammate takes unblocked damage during your turn.",
        [typeof(LizardTail)]       = "Also prevents your teammate from dying.",
        [typeof(EmotionChip)]      = "Also triggered by damage your teammate received last turn.",
    };

    private static readonly Dictionary<Type, string> _cardDescriptions = new()
    {
        [typeof(Spite)]       = "Also triggered by damage your teammate receives.",
        [typeof(TearAsunder)] = "Also triggered by damage your teammate receives.",
    };

    /// <summary>
    /// True when SharedLoseHp is active: uses the locked run setting during a run,
    /// or the local saved setting in lobby/menus.
    /// </summary>
    private static bool SharedLoseHpEnabled =>
        SoulLinkSession.IsActive
            ? SoulLinkSession.ActiveRunSettings.SharedLoseHp
            : SoulLinkSettings.Instance.SharedLoseHp;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
    static void RelicPostfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SharedLoseHpEnabled) return;
        if (!_relicDescriptions.TryGetValue(__instance.GetType(), out var desc)) return;
        __result = __result.Append(new HoverTip(_titleStr, desc));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
    static void CardPostfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SharedLoseHpEnabled) return;
        if (!_cardDescriptions.TryGetValue(__instance.GetType(), out var desc)) return;
        __result = __result.Append(new HoverTip(_titleStr, desc));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
    static void PotionPostfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SharedLoseHpEnabled) return;
        if (__instance is not FairyInABottle) return;
        __result = __result.Append(new HoverTip(_titleStr, "Also saves your teammate from dying."));
    }
}
