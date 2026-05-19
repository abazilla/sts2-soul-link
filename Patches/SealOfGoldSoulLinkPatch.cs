using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.VGQ;

namespace SoulLinkMod.Patches;

/// <summary>
/// Bypasses vanilla <see cref="SealOfGold.AfterSideTurnStart"/> when shared gold is
/// active. The vanilla path calls <c>PlayerCmd.LoseGold</c>, which routes through
/// <c>GoldSyncPatch</c> → <c>RequestEnqueueGoldChange</c>. <c>RequestEnqueue</c>
/// executes synchronously on the host (advancing an extra checksum slot inside the
/// hook) but is delivered later as a network message to clients — the off-by-one
/// causes a checksum divergence on the next turn-start.
///
/// VGQ-aligned fix: every peer enqueues an identical
/// <see cref="SoulLinkSealOfGoldGameAction"/> via
/// <c>ActionQueueSet.EnqueueWithoutSynchronizing</c> from inside the deterministic
/// hook. No host→client broadcast, identical queue position on every peer,
/// gold + energy applied in a single ExecuteAction.
/// </summary>
[HarmonyPatch(typeof(SealOfGold), nameof(SealOfGold.AfterSideTurnStart))]
public static class SealOfGoldSoulLinkPatch
{
    [HarmonyPrefix]
    static bool Prefix(SealOfGold __instance, CombatSide side, CombatState combatState, ref Task __result)
    {
        if (!SoulLinkSession.IsActive) return true;
        if (SoulLinkSession.ActiveRunSettings.GoldMode == GoldSharingMode.Default) return true;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)) return true;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing)) return true;

        var owner = __instance.Owner;
        if (owner == null || side != owner.Creature.Side)
        {
            __result = Task.CompletedTask;
            return false;
        }

        int goldCost = __instance.DynamicVars.Gold.IntValue;
        int energyGain = __instance.DynamicVars.Energy.BaseValue;

        int canonicalGold = SoulLinkSession.ActiveRunSettings.GoldMode == GoldSharingMode.SplitByPlayer
            ? SoulLinkSession.GetPlayerGold(ResolveSlot(owner))
            : SoulLinkSession.Gold;
        if (canonicalGold < goldCost)
        {
            __result = Task.CompletedTask;
            return false;
        }

        int slot = ResolveSlot(owner);
        if (slot < 0)
        {
            __result = Task.CompletedTask;
            return false;
        }

        __instance.Flash();

        var action = new SoulLinkSealOfGoldGameAction(slot,
            SoulLinkSession.ActiveRunSettings.GoldMode, goldCost, energyGain);

        // Per-peer enqueue: every peer runs this hook deterministically with identical
        // pre-state, so each ActionQueueSet bumps _nextId in lockstep and the action
        // lands at the same ID on every machine.
        RunManager.Instance?.ActionQueueSet?.EnqueueWithoutSynchronizing(action);

        __result = Task.CompletedTask;
        return false;
    }

    private static int ResolveSlot(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs == null) return -1;
        for (int i = 0; i < rs.Players.Count; i++)
            if (rs.Players[i] == player) return i;
        return -1;
    }
}
