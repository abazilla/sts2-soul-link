using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod;

/// <summary>
/// Service for managing INetAction sending and receiving.
///
/// Provides centralized handling of action lifecycle:
/// - Sending actions from local client
/// - Receiving and deserializing actions from peers
/// - Executing actions with proper context
/// - Maintaining action history for debugging
///
/// Actions are serialized as custom network messages and dispatched through
/// the standard message handler infrastructure.
/// </summary>
public static class NetActionService
{
    /// <summary>
    /// Delegates for receiving specific action types.
    /// </summary>
    private static readonly Dictionary<Type, Delegate> _handlers = new();

    /// <summary>
    /// History of executed actions for debugging and replay.
    /// </summary>
    private static readonly LinkedList<(INetAction action, INetActionContext context)> _history = new();
    private const int HistoryCapacity = 100;

    /// <summary>
    /// Whether we're currently executing an action to prevent re-entrancy loops.
    /// </summary>
    private static bool _executingAction;

    /// <summary>
    /// Registers a handler for a specific action type.
    /// Called once per action type to set up message reception.
    /// </summary>
    /// <typeparam name="T">The action type to handle</typeparam>
    /// <param name="handler">Handler that receives the action and sender ID</param>
    internal static void RegisterActionHandler<T>(Action<T, ulong> handler) where T : struct, INetAction
    {
        _handlers[typeof(T)] = handler;
        GD.Print($"[NetActionService] Registered handler for {typeof(T).Name}");
    }

    /// <summary>
    /// Unregisters a handler for a specific action type.
    /// </summary>
    /// <typeparam name="T">The action type to unregister</typeparam>
    internal static void UnregisterActionHandler<T>() where T : struct, INetAction
    {
        _handlers.Remove(typeof(T));
        GD.Print($"[NetActionService] Unregistered handler for {typeof(T).Name}");
    }

    /// <summary>
    /// Enqueues a local action to be executed immediately and broadcast to peers.
    /// </summary>
    /// <typeparam name="T">The action type</typeparam>
    /// <param name="action">The action to enqueue</param>
    /// <param name="playerSlot">The originating player slot (-1 if not player-specific)</param>
    public static void EnqueueLocalAction<T>(T action, int playerSlot = -1) where T : struct, INetAction
    {
        if (_executingAction)
        {
            GD.PrintErr($"[NetActionService] Re-entrancy detected while executing action. Skipping {typeof(T).Name}");
            return;
        }

        var context = NetActionContext.CreateLocal(playerSlot);
        ExecuteAction(action, context, isLocal: true);

        // Broadcast to other peers
        if (action.ShouldBroadcast)
        {
            RunManager.Instance?.NetService?.SendMessage(new NetActionMessage<T>
            {
                Action = action,
                Timestamp = context.Timestamp,
                PlayerSlot = playerSlot,
            });
        }
    }

    /// <summary>
    /// Called when a remote action is received.
    /// Deserializes and executes the action in the proper context.
    /// </summary>
    internal static void ExecuteRemoteAction<T>(T action, long timestamp, int playerSlot) where T : struct, INetAction
    {
        var context = NetActionContext.CreateRemote(playerSlot, timestamp);
        ExecuteAction(action, context, isLocal: false);
    }

    /// <summary>
    /// Executes an action with the given context.
    /// Maintains history and enforces re-entrancy protection.
    /// </summary>
    private static void ExecuteAction<T>(T action, INetActionContext context, bool isLocal) where T : struct, INetAction
    {
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SoulLinkEnabled))
            return;

        _executingAction = true;
        try
        {
            // Check if feature is enabled before execution
            if (!FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
            {
                GD.Print($"[NetActionService] NetworkedActions flag disabled. Skipping {typeof(T).Name}");
                return;
            }

            action.Execute(context);

            // Record in history
            AddToHistory(action, context);

            if (action.LogLevel >= LogLevel.Debug)
            {
                GD.Print($"[NetActionService] Executed {typeof(T).Name} (local={isLocal}, slot={context.OriginatingPlayerSlot})");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetActionService] Error executing {typeof(T).Name}: {ex}");
        }
        finally
        {
            _executingAction = false;
        }
    }

    /// <summary>
    /// Adds an action to the history log.
    /// </summary>
    private static void AddToHistory<T>(T action, INetActionContext context) where T : struct, INetAction
    {
        _history.AddLast((action, context));
        if (_history.Count > HistoryCapacity)
            _history.RemoveFirst();
    }

    /// <summary>
    /// Gets the action execution history for debugging.
    /// </summary>
    public static IEnumerable<(INetAction action, INetActionContext context)> GetHistory() => _history;

    /// <summary>
    /// Clears action history and resets service state.
    /// </summary>
    internal static void Reset()
    {
        _history.Clear();
        _executingAction = false;
        GD.Print("[NetActionService] Reset");
    }
}
