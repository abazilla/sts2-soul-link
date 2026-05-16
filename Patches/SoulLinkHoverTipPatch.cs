using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using SoulLinkMod.Localization;
using MethodType = HarmonyLib.MethodType;

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
    // Maps game type -> loc key under the "tip." namespace. Strings live in
    // Localization/locales/*.json so they translate alongside the rest of the UI.
    private static readonly Dictionary<Type, string> _relicKeys = new()
    {
        [typeof(CentennialPuzzle)] = "tip.centennial_puzzle",
        [typeof(SelfFormingClay)]  = "tip.self_forming_clay",
        [typeof(DemonTongue)]      = "tip.demon_tongue",
        [typeof(LizardTail)]       = "tip.lizard_tail",
        [typeof(EmotionChip)]      = "tip.emotion_chip",
    };

    private static readonly Dictionary<Type, string> _cardKeys = new()
    {
        [typeof(Spite)]       = "tip.spite",
        [typeof(TearAsunder)] = "tip.tear_asunder",
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
        if (!_relicKeys.TryGetValue(__instance.GetType(), out var key)) return;
        __result = __result.Append(new HoverTip(Loc.S("tip.title"), Loc.T(key)));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
    static void CardPostfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SharedLoseHpEnabled) return;
        if (!_cardKeys.TryGetValue(__instance.GetType(), out var key)) return;
        __result = __result.Append(new HoverTip(Loc.S("tip.title"), Loc.T(key)));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
    static void PotionPostfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SharedLoseHpEnabled) return;
        if (__instance is not FairyInABottle) return;
        __result = __result.Append(new HoverTip(Loc.S("tip.title"), Loc.T("tip.fairy_in_a_bottle")));
    }
}
