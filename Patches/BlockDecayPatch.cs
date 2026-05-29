using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

using SoulLinkMod;

namespace SoulLinkMod.Patches;

/// <summary>
/// No-op. Originally suppressed vanilla turn-end block decay because the old
/// VGQ-deferred sync had one peer's ClearBlock drain the canonical pool for
/// others. New BlockSyncPatch applies pool delta + canonical mirror inline on
/// every peer, so both peers compute the same drain deterministically and
/// vanilla ClearBlock can flow through normally.
/// </summary>
[HarmonyPatch]
public static class BlockDecayPatch
{
    static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(Creature), "ClearBlock");

    [HarmonyPrefix]
    static bool Prefix(ref Task __result) => true;
}
