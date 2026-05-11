using Godot;
using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.VGQ;

/// <summary>
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link Gold synchronization.
///
/// PROTOTYPE STATUS: This is a scaffold demonstrating VGQ architecture patterns.
/// ToNetAction() and ActionType require further investigation into STS2's internal
/// types (GameActionType enum and INetAction interface location unknown).
///
/// This GameAction subclass integrates with STS2's native action queue to provide
/// deterministic ordering guarantees for Gold changes across multiplayer peers.
///
/// Key differences from MNA GoldChangeAction:
/// - Integrated with vanilla action queue (not separate pipeline)
/// - Uses ToNetAction()/ToGameAction() for network serialization
/// - Relies on queue ordering instead of deduplication dictionaries
///
/// BLOCKERS:
/// - GameActionType enum location unknown (not in GameActions, Context, or Multiplayer namespaces)
/// - INetAction interface location unknown (not in GameActions namespace)
/// - Serialization pattern for custom GameAction subclasses needs research
/// </summary>
// NOTE: Currently cannot compile due to unknown types. Keeping as documentation of intended VGQ architecture.
// To make this compilable, would need to:
// 1. Locate GameActionType enum (checked: GameActions, Context, Multiplayer namespaces - not found)
// 2. Locate INetAction interface for serialization (checked: GameActions namespace - not found)
// 3. Understand vanilla GameAction serialization pattern
//
// For now, commented out to allow project to build. Uncomment when types are located.
/*
public class SoulLinkGoldChangeGameAction : GameAction
{
    public int DeltaGold { get; private set; }
    public int PlayerSlot { get; private set; }
    public GoldSharingMode Mode { get; private set; }
    public string? Source { get; private set; }

    /// <summary>
    /// Constructor for local action creation (host enqueueing the action).
    /// </summary>
    public SoulLinkGoldChangeGameAction(int deltaGold, int playerSlot, GoldSharingMode mode, string? source = null)
    {
        DeltaGold = deltaGold;
        PlayerSlot = playerSlot;
        Mode = mode;
        Source = source;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public SoulLinkGoldChangeGameAction()
    {
    }

    // TODO: Determine correct ActionType enum and value
    // public override GameActionType ActionType => GameActionType.Meta;

    public override ulong OwnerId => 0; // System-owned action (not tied to a specific player)

    /// <summary>
    /// Execute the Gold change deterministically on all peers.
    /// This is called by the action queue when the action is processed.
    /// </summary>
    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            GD.PrintErr("[SoulLink][VGQ] SoulLinkGoldChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing))
            return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][VGQ] Apply failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][VGQ] Apply failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        GD.Print($"[SoulLink][VGQ] Apply Gold: slot={PlayerSlot} delta={DeltaGold} mode={Mode} poolBefore={SoulLinkSession.Gold}{sourceStr}");

        // Apply the delta to the shared or split gold pool based on mode
        int canonicalGold = 0;
        int playerCount = runState.Players.Count;

        if (Mode == GoldSharingMode.SharedPool)
        {
            // SharedPool: all players share one gold pool
            canonicalGold = SoulLinkSession.ApplyGoldDelta(DeltaGold, playerCount, PlayerSlot, false, null, Source);

            // Write canonical gold to all player gold fields
            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                foreach (var player in runState.Players)
                {
                    player.Gold = canonicalGold;
                }
            }
            finally
            {
                SoulLinkMod.ApplyingCanonical = false;
            }
        }
        else if (Mode == GoldSharingMode.SplitByPlayer)
        {
            // SplitByPlayer: each player has their own gold, but gains are split
            // This is a more complex mode that requires per-player gold tracking
            canonicalGold = SoulLinkSession.ApplyGoldDelta(DeltaGold, playerCount, PlayerSlot, false, null, Source);

            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                // Write per-player gold values
                for (int i = 0; i < runState.Players.Count; i++)
                {
                    runState.Players[i].Gold = SoulLinkSession.GetPlayerGold(i);
                }
            }
            finally
            {
                SoulLinkMod.ApplyingCanonical = false;
            }
        }

        // Refresh UI panels
        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
*/
