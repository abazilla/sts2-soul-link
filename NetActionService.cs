using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod;

/// <summary>
/// Service for managing INetAction sending and receiving.
/// Actions are serialized as custom network messages and dispatched through
/// the standard message handler infrastructure.
/// </summary>
public static class NetActionService
{
    private static readonly Dictionary<Type, Delegate> _handlers = new();
    private static readonly LinkedList<(INetAction action, INetActionContext context)> _history = new();
    private const int HistoryCapacity = 100;
    private static bool _executingAction;

    internal static void RegisterActionHandler<T>(Action<T, ulong> handler) where T : struct, INetAction
    {
        _handlers[typeof(T)] = handler;
        GD.Print($"[NetActionService] Registered handler for {typeof(T).Name}");
    }

    internal static void UnregisterActionHandler<T>() where T : struct, INetAction
    {
        _handlers.Remove(typeof(T));
        GD.Print($"[NetActionService] Unregistered handler for {typeof(T).Name}");
    }

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

    internal static void ExecuteRemoteAction<T>(T action, long timestamp, int playerSlot) where T : struct, INetAction
    {
        var context = NetActionContext.CreateRemote(playerSlot, timestamp);
        ExecuteAction(action, context, isLocal: false);
    }

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

    private static void AddToHistory<T>(T action, INetActionContext context) where T : struct, INetAction
    {
        _history.AddLast((action, context));
        if (_history.Count > HistoryCapacity)
            _history.RemoveFirst();
    }

    public static IEnumerable<(INetAction action, INetActionContext context)> GetHistory() => _history;

    internal static void Reset()
    {
        _history.Clear();
        _executingAction = false;
        GD.Print("[NetActionService] Reset");
    }
}
