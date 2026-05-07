using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.MaxHp setter to intercept max-HP changes on player creatures.
///
/// Out-of-combat max-HP gains are divided by player count (same scaling rule as heals).
/// Writes the canonical MaxHp (and possibly-clamped CurrentHp) back to all players
/// via CreatureHelper reflection, since the setters are private.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class MaxHpSyncPatch
{
    static MethodBase TargetMethod()
        => AccessTools.PropertySetter(typeof(Creature), nameof(Creature.MaxHp));

    static void Prefix(Creature __instance, ref int value)
    {
        if (SoulLinkMod.ApplyingCanonical) return;
        if (!SoulLinkSession.IsActive) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        int playerSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (runState.Players[i].Creature == __instance)
            {
                playerSlot = i;
                break;
            }
        }
        if (playerSlot < 0) return;

        int delta = value - __instance.MaxHp;
        if (delta == 0) return;

        bool inCombat = CombatManager.Instance.IsInProgress;
        int playerCount = runState.Players.Count;

        string? source = SoulLinkSession.PendingSource
            ?? SoulLinkSession.CurrentRoomSource
            ?? (inCombat ? "Combat" : "Out of combat");
        SoulLinkSession.PendingSource = null;
        SoulLinkSession.ApplyMaxHpDelta(delta, inCombat, playerCount, playerSlot, source);

        // Redirect the write on the triggering player.
        value = SoulLinkSession.MaxHp;

        // Write canonical MaxHp and CurrentHp to all other players.
        // The triggering creature gets MaxHp via the redirected `value` above.
        // Its CurrentHp will be updated by the follow-up CurrentHp write the game fires
        // immediately after (the heal equal to the MaxHp gain), intercepted by HpSyncPatch.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                if (player.Creature != __instance)
                {
                    player.Creature.SetMaxHp(SoulLinkSession.MaxHp);
                    player.Creature.SetCurrentHp(SoulLinkSession.CurrentHp);
                }
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Broadcast the MaxHP change.
        if (FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
        {
            NetActionService.EnqueueLocalAction(new MaxHpChangeAction
            {
                DeltaMaxHp = delta,
                PlayerSlot = playerSlot,
                InCombat = inCombat,
                Source = source,
            }, playerSlot);
        }

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
