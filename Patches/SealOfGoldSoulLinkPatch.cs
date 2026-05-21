using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.VGQ;

namespace SoulLinkMod.Patches;

/// <summary>
/// Suppresses vanilla <see cref="SealOfGold.AfterSideTurnStart"/> on every peer when
/// shared-gold is active. Per-peer hooks fire only for locally-owned relics, so any
/// per-peer enqueue path produces divergent action IDs across peers (same id label,
/// different action). All enqueues are funneled through a single host-only orchestrator
/// in <see cref="SealOfGoldHostOrchestratorPatch"/>.
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

        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>
/// Host-only SealOfGold orchestrator. Postfixes <see cref="Hook.AfterSideTurnStart"/>
/// (single seam, deterministic relative to the action queue, fires once per side-turn).
/// Host iterates every player, enqueues one <see cref="SoulLinkSealOfGoldGameAction"/>
/// per SealOfGold owner on the matching side via
/// <see cref="ActionQueueSynchronizer.RequestEnqueue"/>. Host's RequestEnqueue broadcasts
/// an <c>ActionEnqueuedMessage</c> with the current message-buffer location, so every
/// client enqueues the same actions at the same location — identical action IDs across
/// peers, no checksum divergence.
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnStart))]
public static class SealOfGoldHostOrchestratorPatch
{
    [HarmonyPostfix]
    static void Postfix(CombatState combatState, CombatSide side, ref Task __result)
    {
        var original = __result;
        __result = WrapAsync(original, combatState, side);
    }

    static async Task WrapAsync(Task original, CombatState combatState, CombatSide side)
    {
        await original;

        if (!SoulLinkSession.IsActive) return;
        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;
        if (goldMode == GoldSharingMode.Default) return;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)) return;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing)) return;

        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs == null || rs.Players.Count == 0) return;

        // Host = peer that owns slot 0. Matches existing RunStartPatch convention.
        if (!LocalContext.IsMe(rs.Players[0])) return;

        for (int slot = 0; slot < rs.Players.Count; slot++)
        {
            var player = rs.Players[slot];
            if (player?.Creature == null) continue;
            if (player.Creature.Side != side) continue;

            var seal = player.Relics.FirstOrDefault(r => r is SealOfGold) as SealOfGold;
            if (seal == null) continue;

            int goldCost = seal.DynamicVars.Gold.IntValue;
            int energyGain = seal.DynamicVars.Energy.IntValue;

            int canonicalGold = goldMode == GoldSharingMode.SplitByPlayer
                ? SoulLinkSession.GetPlayerGold(slot)
                : SoulLinkSession.Gold;
            if (canonicalGold < goldCost) continue;

            var action = new SoulLinkSealOfGoldGameAction(slot, goldMode, goldCost, energyGain);
            ActionQueueSynchronizer.RequestEnqueue(action);
        }
    }
}
