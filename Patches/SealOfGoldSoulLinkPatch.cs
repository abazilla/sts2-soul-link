using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Suppresses vanilla <see cref="SealOfGold.AfterSideTurnStart"/> on every peer when
/// shared-gold is active. Vanilla applies its gold loss through the <c>Player.Gold</c>
/// setter, which <see cref="GoldSyncPatch"/> would turn into a per-peer VGQ broadcast —
/// and with a shared pool that double-counts / diverges. The pooled effect is applied
/// instead by <see cref="SealOfGoldInlineApplierPatch"/>.
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
/// Applies SealOfGold's per-turn effect (lose <c>Gold</c> from the shared pool → gain
/// <c>Energy</c>) inline, identically on every peer, during the single
/// <see cref="Hook.AfterSideTurnStart"/> seam.
///
/// WHY INLINE, NOT A QUEUED ACTION: <c>CombatManager.StartTurn</c> awaits
/// <c>Hook.AfterSideTurnStart</c> (the seam this postfix extends) and only afterwards
/// generates the inline <c>GenerateChecksum("After player turn start")</c>. A previous
/// implementation enqueued a <c>SoulLinkSealOfGoldGameAction</c> from this region: the
/// host drained it synchronously inside StartTurn while clients drained it from an async
/// network broadcast, so the queued action straddled the inline turn-start checksum at a
/// nondeterministic position relative to it — peers diverged (sometimes only after a few
/// turns, when packet timing shifted).
///
/// Mirroring vanilla, the effect is now applied inline before that checksum as a pure
/// function of already-synced state (the shared gold pool, player count, slot). Every
/// peer runs the identical loop over the shared run state, so the turn-start checksum
/// captures identical state on all peers — no queue, no broadcast, no race.
///
/// Gold is written under <see cref="SoulLinkMod.ApplyingCanonical"/> so the
/// <see cref="GoldSyncPatch"/> setter passes the canonical value through without emitting
/// its own broadcast. Energy uses the vanilla <see cref="PlayerCmd.GainEnergy"/> helper,
/// which mutates <c>PlayerCombatState</c> directly (no queue).
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnStart))]
public static class SealOfGoldInlineApplierPatch
{
    [HarmonyPostfix]
    static void Postfix(CombatState combatState, CombatSide side, ref Task __result)
    {
        var original = __result;
        __result = WrapAsync(original, side);
    }

    static async Task WrapAsync(Task original, CombatSide side)
    {
        await original;

        if (!SoulLinkSession.IsActive) return;
        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;
        if (goldMode == GoldSharingMode.Default) return;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)) return;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing)) return;

        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs == null || rs.Players.Count == 0) return;

        int playerCount = rs.Players.Count;
        const string source = "@relic:SealOfGold";
        bool applied = false;

        // Apply identically on EVERY peer (no host-only gate, no broadcast): the result is
        // a pure function of synced pool state, so peers stay in lockstep for the
        // "After player turn start" checksum that follows this hook.
        for (int slot = 0; slot < playerCount; slot++)
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

            seal.Flash();

            int canonical = SoulLinkSession.ApplyGoldDelta(-goldCost, playerCount, slot, false, null, source, goldMode);

            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                if (goldMode == GoldSharingMode.SharedPool)
                {
                    foreach (var p in rs.Players)
                        p.Gold = canonical;
                }
                else if (goldMode == GoldSharingMode.SplitByPlayer)
                {
                    for (int i = 0; i < rs.Players.Count; i++)
                        rs.Players[i].Gold = SoulLinkSession.GetPlayerGold(i);
                }
            }
            finally
            {
                SoulLinkMod.ApplyingCanonical = false;
            }

            await PlayerCmd.GainEnergy(energyGain, player);
            applied = true;
        }

        // ApplyGoldDelta already appended the gold LogEntry; repaint the feed/panels so
        // the spend shows up (AddEntry only mutates state, it does not refresh UI).
        if (applied)
        {
            CombatLogPanel.Current?.Refresh();
            RunStatsPanel.Current?.Refresh();
            DebugOverlay.Current?.Refresh();
        }
    }
}
