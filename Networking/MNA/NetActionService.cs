using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.Messages;

namespace SoulLinkMod;

/// <summary>
/// Service for managing INetAction sending and receiving.
/// Actions are serialized as concrete network messages and dispatched through
/// the standard message handler infrastructure.
/// </summary>
public static class NetActionService
{
    private static readonly Dictionary<Type, Delegate> _handlers = new();
    private static readonly LinkedList<(INetAction action, INetActionContext context)> _history = new();
    private const int HistoryCapacity = 100;
    private static bool _executingAction;
    private static readonly Queue<Action> _pendingMessages = new();

    internal static void RegisterActionHandler<T>(Action<T, ulong> handler) where T : struct, INetAction
    {
        _handlers[typeof(T)] = handler;
        SoulLinkLog.Debug($"[NetActionService] Registered handler for {typeof(T).Name}");
    }

    internal static void UnregisterActionHandler<T>() where T : struct, INetAction
    {
        _handlers.Remove(typeof(T));
        SoulLinkLog.Debug($"[NetActionService] Unregistered handler for {typeof(T).Name}");
    }

    public static void EnqueueLocalAction<T>(T action, int playerSlot = -1) where T : struct, INetAction
    {
        if (_executingAction)
        {
            SoulLinkLog.Error($"[NetActionService] Re-entrancy detected while executing action. Skipping {typeof(T).Name}");
            return;
        }

        var context = NetActionContext.CreateLocal(playerSlot);
        ExecuteAction(action, context, isLocal: true);

        if (!action.ShouldBroadcast)
            return;

        var netService = RunManager.Instance?.NetService;
        if (netService == null)
            return;

        switch (action)
        {
            case HpChangeAction hp:
                netService.SendMessage(new HpChangeSyncMessage
                {
                    DeltaHp = hp.DeltaHp,
                    PlayerSlot = hp.PlayerSlot,
                    InCombat = hp.InCombat,
                    Source = hp.Source,
                    Timestamp = context.Timestamp,
                });
                break;

            case MaxHpChangeAction maxHp:
                netService.SendMessage(new MaxHpChangeSyncMessage
                {
                    DeltaMaxHp = maxHp.DeltaMaxHp,
                    PlayerSlot = maxHp.PlayerSlot,
                    InCombat = maxHp.InCombat,
                    Source = maxHp.Source,
                    Timestamp = context.Timestamp,
                });
                break;

            case GoldChangeAction gold:
                netService.SendMessage(new GoldChangeSyncMessage
                {
                    DeltaGold = gold.DeltaGold,
                    PlayerSlot = gold.PlayerSlot,
                    Source = gold.Source,
                    WasBlocked = gold.WasBlocked,
                    Timestamp = context.Timestamp,
                });
                break;

            default:
                SoulLinkLog.Error($"[NetActionService] Unknown action type: {typeof(T).Name}. Cannot broadcast.");
                break;
        }
    }

    internal static void ExecuteRemoteAction<T>(T action, long timestamp, int playerSlot) where T : struct, INetAction
    {
        // If an action is currently executing, queue this message to prevent parallel state modification
        if (_executingAction)
        {
            _pendingMessages.Enqueue(() => ExecuteRemoteAction(action, timestamp, playerSlot));
            SoulLinkLog.Debug($"[NetActionService] Queued {typeof(T).Name} during action execution (queue size: {_pendingMessages.Count})");
            return;
        }

        var context = NetActionContext.CreateRemote(playerSlot, timestamp);
        ExecuteAction(action, context, isLocal: false);
    }

    private static void DrainPendingMessages()
    {
        if (_pendingMessages.Count == 0)
            return;

        SoulLinkLog.Debug($"[NetActionService] Draining {_pendingMessages.Count} queued messages");

        while (_pendingMessages.Count > 0)
        {
            var messageHandler = _pendingMessages.Dequeue();
            messageHandler();
        }
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
                SoulLinkLog.Debug($"[NetActionService] NetworkedActions flag disabled. Skipping {typeof(T).Name}");
                return;
            }

            action.Execute(context);

            // Record in history
            AddToHistory(action, context);

            if (action.LogLevel >= LogLevel.Debug)
            {
                SoulLinkLog.Debug($"[NetActionService] Executed {typeof(T).Name} (local={isLocal}, slot={context.OriginatingPlayerSlot})");
            }
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"[NetActionService] Error executing {typeof(T).Name}: {ex}");
            DumpHistory($"Exception in {typeof(T).Name}");
        }
        finally
        {
            _executingAction = false;
            DrainPendingMessages();
        }
    }

    private static void AddToHistory<T>(T action, INetActionContext context) where T : struct, INetAction
    {
        _history.AddLast((action, context));
        if (_history.Count > HistoryCapacity)
            _history.RemoveFirst();
    }

    public static IEnumerable<(INetAction action, INetActionContext context)> GetHistory() => _history;

    public static void DumpHistory(string reason)
    {
        SoulLinkLog.Error($"[HISTORY DUMP] Reason: {reason}");
        SoulLinkLog.Error($"[HISTORY DUMP] {_history.Count} actions:");
        int i = 0;
        foreach (var (action, ctx) in _history)
        {
            string type = action.GetType().Name;
            string local = ctx.IsLocal ? "LOCAL" : "REMOTE";
            string slot = $"slot={ctx.OriginatingPlayerSlot}";

            string details = action switch
            {
                HpChangeAction hp => $"delta={hp.DeltaHp} inCombat={hp.InCombat} src={hp.Source}",
                MaxHpChangeAction mhp => $"delta={mhp.DeltaMaxHp} inCombat={mhp.InCombat} src={mhp.Source}",
                GoldChangeAction g => $"delta={g.DeltaGold} blocked={g.WasBlocked} src={g.Source}",
                _ => action.ToString() ?? type
            };

            SoulLinkLog.Error($"  [{i++}] {type} {local} {slot}: {details}");
        }
    }

    internal static void Reset()
    {
        if (_history.Count > 0)
            DumpHistory("Session reset (likely disconnect)");
        _history.Clear();
        _executingAction = false;
        _pendingMessages.Clear();
        SyncCoordinator.Reset();
        SoulLinkLog.Debug("[NetActionService] Reset");
    }
}
