using System.Reflection;
using HarmonyLib;

namespace SoulLinkMod.Patches;

/// <summary>
/// Suppresses VGQ broadcast during BurningBlood.AfterCombatVictory.
/// The heal is deterministic (fires on every peer), so local HpSyncPatch handles it.
/// </summary>
[HarmonyPatch]
public static class BurningBloodSoulLinkPatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method("MegaCrit.Sts2.Core.Models.Relics.BurningBlood:AfterCombatVictory");

    [HarmonyPrefix]
    static void Prefix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkMod.SuppressVgqEnqueue = true;
        SoulLinkSession.PendingSource = "BurningBlood";
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkMod.SuppressVgqEnqueue = false;
    }
}
