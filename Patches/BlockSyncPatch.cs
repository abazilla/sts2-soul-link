using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.VGQ;

using SoulLinkMod;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.Block setter to route shared-block changes through VGQ.
///
/// Active only when HpMode == SharedHpAndBlock and combat is in progress. The
/// originating peer enqueues a SoulLinkBlockChangeGameAction carrying the delta;
/// ExecuteAction applies the delta to the canonical pool and mirrors the value
/// to every peer's creature.Block under the ApplyingCanonical guard.
///
/// Out-of-combat block writes (vanilla zero on combat end) are passed through
/// untouched — the combat-enter reset in CombatRoomReadyPatch handles fresh state.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class BlockSyncPatch
{
    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Creature), nameof(Creature.Block));

    static bool Prefix(Creature __instance, ref int value)
    {
        if (SoulLinkMod.ApplyingCanonical) return true;
        if (!SoulLinkSession.IsActive) return true;
        if (!SoulLinkSession.IsSharedBlockEnabled) return true;
        if (!SoulLinkMod.IsCombatActive()) return true;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync)) return true;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return true;

        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return true; // monster creature — let vanilla handle

        int delta = value - __instance.Block;
        if (delta == 0) return true;

        bool isLocalPlayer = LocalContext.IsMe(runState.Players[playerSlot]);

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource;
        SoulLinkSession.PendingSource = null;

        SoulLinkLog.Info($"[BlockSync] slot={playerSlot} delta={delta} isLocal={isLocalPlayer} poolBefore={SoulLinkSession.SharedBlock} source={source}");

        // Combat damage and Defend card plays are deterministic across peers — every
        // peer's vanilla setter fires with the same delta. Apply pool change + mirror
        // canonical to every creature synchronously on every peer so subsequent damage
        // math reads correct post-mirror block. Skip VGQ network round-trip: relying
        // on owner-only VGQ left non-owner creatures stale (slot teammates kept old
        // block until a deferred VGQ from owner ran), causing second-hit damage math
        // to diverge between host and client.
        int canonical = SoulLinkSession.ApplyBlockDelta(delta, playerSlot, source);
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
        value = canonical;
        return false;
    }
}
