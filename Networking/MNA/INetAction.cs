using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

/// <summary>
/// Contract for networked game actions that need to be synchronized across all peers.
///
/// Similar to INetMessage but specifically for game state mutations that must execute
/// in a deterministic order on all clients. Actions implementing this interface will
/// be automatically serialized, transmitted, and executed on remote peers.
///
/// Key differences from INetMessage:
/// - INetAction represents a state-changing operation (e.g., gain gold, heal, damage)
/// - INetMessage represents a notification or data transfer (e.g., sync settings, update UI)
///
/// Implementations should:
/// 1. Implement IPacketSerializable for network transport
/// 2. Provide an Execute() method that applies the action's effects
/// 3. Be deterministic - same inputs produce same outputs on all clients
/// 4. Handle feature flags appropriately (check before execution)
///
/// STS2 will auto-register implementations via ReflectionHelper.GetSubtypesInMods&lt;INetAction&gt;().
/// </summary>
public interface INetAction : IPacketSerializable
{
    /// <summary>
    /// Whether this action should be broadcast to all peers (true) or sent point-to-point (false).
    /// Most multiplayer actions should broadcast to ensure all clients see the state change.
    /// </summary>
    bool ShouldBroadcast { get; }

    /// <summary>
    /// Network transfer mode for this action.
    /// - Reliable: guaranteed delivery, use for critical state changes
    /// - ReliableOrdered: guaranteed delivery in order, use for sequential actions
    /// - Unreliable: best-effort, use only for non-critical updates
    ///
    /// Default to Reliable or ReliableOrdered for state-changing actions.
    /// </summary>
    NetTransferMode Mode { get; }

    /// <summary>
    /// Logging level for network transmission of this action.
    /// Debug: verbose logging for development
    /// Info: important state changes
    /// Warning: unusual conditions
    /// Error: failures only
    /// </summary>
    LogLevel LogLevel { get; }

    /// <summary>
    /// Executes this action's effects on the current machine.
    ///
    /// This method is called:
    /// - On the originating client after creating the action
    /// - On remote clients after receiving and deserializing the action
    ///
    /// Implementations must be deterministic and idempotent where possible.
    /// Check feature flags before applying effects that may be disabled.
    /// </summary>
    /// <param name="context">Execution context containing player, run state, etc.</param>
    void Execute(INetActionContext context);
}

/// <summary>
/// Context provided to INetAction.Execute() containing runtime information
/// needed to apply the action's effects.
/// </summary>
public interface INetActionContext
{
    /// <summary>
    /// The player slot (index) that originated this action.
    /// -1 if the action is not player-specific (e.g., global game state change).
    /// </summary>
    int OriginatingPlayerSlot { get; }

    /// <summary>
    /// Whether this action is being executed on the local machine (vs. remote peer).
    /// Useful for distinguishing between local vs. network-initiated execution.
    /// </summary>
    bool IsLocal { get; }

    /// <summary>
    /// Timestamp when this action was created (for ordering and deduplication).
    /// </summary>
    long Timestamp { get; }
}
