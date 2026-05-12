using Godot;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.VGQ;

/// <summary>
/// Facade for enqueueing VGQ (Vanilla GameAction Queue) actions.
///
/// This provides a thin API layer for patches to enqueue Soul Link actions
/// into the vanilla action queue without needing to know queue internals.
///
/// The vanilla game's ActionQueueSynchronizer handles deterministic ordering
/// and multiplayer synchronization of GameActions.
///
/// Example usage from HpSyncPatch:
///   ActionQueueSynchronizer.RequestEnqueue(new SoulLinkHpChangeGameAction(...));
/// </summary>
public static class ActionQueueSynchronizer
{
    /// <summary>
    /// Enqueue a Soul Link action into the vanilla GameAction queue.
    ///
    /// This is the main entry point for patches to synchronize state changes
    /// using the VGQ architecture.
    /// </summary>
    public static void RequestEnqueue(GameAction action)
    {
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            GD.PrintErr("[SoulLink][VGQ] RequestEnqueue called but UseVGQSync is disabled");
            return;
        }

        var runManager = RunManager.Instance;
        if (runManager == null)
        {
            GD.PrintErr("[SoulLink][VGQ] RequestEnqueue failed: RunManager.Instance is null");
            return;
        }

        // Use the vanilla game's ActionQueueSynchronizer to enqueue our custom GameAction.
        // The game's ActionQueueSynchronizer.RequestEnqueue handles:
        // - Multiplayer synchronization (broadcasts to all clients)
        // - Deterministic action ID assignment
        // - Queue ordering guarantees
        runManager.ActionQueueSynchronizer.RequestEnqueue(action);

        GD.Print($"[SoulLink][VGQ] Enqueued {action.GetType().Name} to vanilla action queue");
    }

    /// <summary>
    /// Convenience method for enqueueing HP change actions.
    /// </summary>
    public static void RequestEnqueueHpChange(int deltaHp, int playerSlot, bool inCombat, string? source = null)
    {
        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return;
        RequestEnqueue(new SoulLinkHpChangeGameAction(deltaHp, playerSlot, inCombat, source));
    }

    /// <summary>
    /// Convenience method for enqueueing MaxHP change actions.
    /// </summary>
    public static void RequestEnqueueMaxHpChange(int deltaMaxHp, int playerSlot, bool inCombat, string? source = null)
    {
        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return;
        RequestEnqueue(new SoulLinkMaxHpChangeGameAction(deltaMaxHp, playerSlot, inCombat, source));
    }

    /// <summary>
    /// Convenience method for enqueueing Gold change actions.
    /// </summary>
    public static void RequestEnqueueGoldChange(int deltaGold, int playerSlot, GoldSharingMode mode, bool inCombat, string? source = null)
    {
        if (mode == GoldSharingMode.Default) return;
        RequestEnqueue(new SoulLinkGoldChangeGameAction(deltaGold, playerSlot, mode, inCombat, source));
    }
}
