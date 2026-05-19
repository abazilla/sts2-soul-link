using System.Reflection;
using HarmonyLib;

namespace SoulLinkMod.Patches;

/// <summary>
/// Suppresses VGQ broadcast during AncientEventModel.BeforeEventStarted.
/// The heal-to-80% (WearyTraveler ascension) fires per-peer (once per player creature),
/// so only the first peer's heal should land on the shared pool.
/// </summary>
[HarmonyPatch]
public static class AncientEventHealSoulLinkPatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method("MegaCrit.Sts2.Core.Models.AncientEventModel:BeforeEventStarted");

    [HarmonyPrefix]
    static void Prefix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkMod.SuppressVgqEnqueue = true;
        SoulLinkSession.PendingSource = "@event:AncientHeal";
        SoulLinkSession.BeginPerPeerHeal();
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkMod.SuppressVgqEnqueue = false;
        SoulLinkSession.PerPeerDeterministicHeal = false;
    }
}
