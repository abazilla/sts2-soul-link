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
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link HP synchronization.
///
/// PROTOTYPE STATUS: This is a scaffold demonstrating VGQ architecture patterns.
/// ToNetAction() and ActionType require further investigation into STS2's internal
/// types (GameActionType enum and INetAction interface location unknown).
///
/// This GameAction subclass integrates with STS2's native action queue to provide
/// deterministic ordering guarantees for HP changes across multiplayer peers.
///
/// Key differences from MNA HpChangeAction:
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
public class SoulLinkHpChangeGameAction : GameAction
{
    public int DeltaHp { get; private set; }
    public int PlayerSlot { get; private set; }
    public bool InCombat { get; private set; }
    public string? Source { get; private set; }

    /// <summary>
    /// Constructor for local action creation (host enqueueing the action).
    /// </summary>
    public SoulLinkHpChangeGameAction(int deltaHp, int playerSlot, bool inCombat, string? source = null)
    {
        DeltaHp = deltaHp;
        PlayerSlot = playerSlot;
        InCombat = inCombat;
        Source = source;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public SoulLinkHpChangeGameAction()
    {
    }

    // TODO: Determine correct ActionType enum and value
    // public override GameActionType ActionType => GameActionType.Meta;

    public override ulong OwnerId => 0; // System-owned action (not tied to a specific player)

    /// <summary>
    /// Execute the HP change deterministically on all peers.
    /// This is called by the action queue when the action is processed.
    /// </summary>
    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            GD.PrintErr("[SoulLink][VGQ] SoulLinkHpChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SharedHealthPool))
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
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        GD.Print($"[SoulLink][VGQ] Apply: slot={PlayerSlot} delta={DeltaHp} poolBefore={SoulLinkSession.CurrentHp}{sourceStr}{combatStr}");

        int playerCount = runState.Players.Count;

        // Re-evaluate InCombat flag at execution time (not message send time)
        bool actualInCombat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false;
        if (actualInCombat != InCombat)
        {
            GD.Print($"[SoulLink][VGQ] InCombat flag mismatch: action={InCombat}, actual={actualInCombat}. Using actual.");
        }

        // Apply the delta to the shared HP pool
        int canonical = SoulLinkSession.ApplyHpDelta(DeltaHp, actualInCombat, playerCount, Source);

        // Write canonical HP to all player creatures
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetCurrentHp(canonical);
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Refresh UI panels
        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
*/
