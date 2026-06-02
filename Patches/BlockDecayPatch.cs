using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

using SoulLinkMod;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches vanilla turn-end block decay for the shared block pool.
///
/// Vanilla ClearBlock fires per-creature. Retention effects (Barricade, Blur,
/// SturdyClamp) check owner == creature, so only the owner's creature is
/// protected — a teammate's ClearBlock drains the shared pool regardless.
///
/// When shared block is active, this patch scans ALL peer creatures before
/// allowing any single creature's clear:
///   1. Any peer has BarricadePower or BlurPower → full retention (skip clear)
///   2. Else any peer has SturdyClamp → cap pool at 10
///   3. Else → vanilla clear (pool to 0)
///
/// Barricade trumps SturdyClamp. BlurPower counter decrement is independent
/// (AfterSideTurnStart), unaffected by this patch.
/// </summary>
[HarmonyPatch]
public static class BlockDecayPatch
{
    private const int SturdyClampCap = 10;

    static MethodBase? TargetMethod()
        => AccessTools.Method(typeof(Creature), "ClearBlock");

    [HarmonyPrefix]
    static bool Prefix(Creature __instance, ref Task __result)
    {
        if (!SoulLinkSession.IsActive) return true;
        if (!SoulLinkSession.IsSharedBlockEnabled) return true;
        if (!SoulLinkMod.IsCombatActive()) return true;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return true;

        bool isPlayerCreature = false;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == __instance)
            {
                isPlayerCreature = true;
                break;
            }
        }
        if (!isPlayerCreature) return true;

        var combatState = __instance.CombatState;
        if (combatState == null) return true;

        bool hasFullRetention = false;
        bool hasSturdyClamp = false;

        foreach (var player in runState.Players)
        {
            if (Hook.ShouldClearBlock(combatState, player.Creature, out var preventer))
                continue;

            if (preventer is BarricadePower or BlurPower)
            {
                hasFullRetention = true;
                break;
            }
            if (preventer is SturdyClamp)
                hasSturdyClamp = true;
        }

        if (hasFullRetention)
        {
            SoulLinkLog.Debug($"[BlockDecay] Full retention (Barricade/Blur) — skipping clear for {__instance.Name}");
            __result = Task.CompletedTask;
            return false;
        }

        if (hasSturdyClamp)
        {
            int pool = SoulLinkSession.SharedBlock;
            if (pool > SturdyClampCap)
            {
                int canonical = SoulLinkSession.ApplyBlockDelta(-(pool - SturdyClampCap), -1, "SturdyClamp");
                SoulLinkLog.Debug($"[BlockDecay] SturdyClamp cap — pool {pool} → {canonical}");
                SoulLinkMod.ApplyingCanonical = true;
                try
                {
                    foreach (var player in runState.Players)
                        player.Creature.SetBlock(canonical);
                }
                finally
                {
                    SoulLinkMod.ApplyingCanonical = false;
                }
            }
            __result = Task.CompletedTask;
            return false;
        }

        // No retention across any peer — let vanilla clear proceed.
        // BlockSyncPatch handles the pool delta from Block = 0.
        return true;
    }
}
