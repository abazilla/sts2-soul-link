using Godot;
using System.Linq;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.VGQ;

/// <summary>
/// Facade for enqueueing VGQ (Vanilla GameAction Queue) actions.
///
/// STATUS: Active with reflection-based API discovery. Uncommented and wired into all sync patches.
/// Uses runtime reflection to discover the correct RunManager queueing method until the proper
/// API is identified via sts2-modding MCP server or game documentation.
///
/// This provides a thin API layer for patches to enqueue Soul Link actions
/// into the vanilla action queue without needing to know queue internals.
///
/// Example usage from HpSyncPatch:
///   ActionQueueSynchronizer.RequestEnqueue(new SoulLinkHpChangeGameAction(...));
///
/// DISCOVERY APPROACH:
/// - Logs all methods matching Queue/Action/Enqueue/Add patterns on RunManager
/// - Logs all properties matching Queue/Action patterns if no methods found
/// - Returns early with error until correct API is discovered
///
/// NEXT STEPS:
/// - Use sts2-modding MCP server to find correct method signature
/// - Replace reflection discovery with actual method call
/// - Test in FastMP to verify deterministic action execution
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

        // Try common API patterns before falling back to reflection
        // Pattern 1: Static method on GameAction
        try
        {
            var queueMethod = typeof(GameAction).GetMethod("Queue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (queueMethod != null)
            {
                queueMethod.Invoke(null, new object[] { action });
                GD.Print($"[SoulLink][VGQ] Successfully enqueued {action.GetType().Name} via GameAction.Queue()");
                return;
            }
        }
        catch { }

        // Pattern 2: Instance method on RunManager
        try
        {
            var queueMethod = runManager.GetType().GetMethod("Queue", new[] { typeof(GameAction) });
            if (queueMethod != null)
            {
                queueMethod.Invoke(runManager, new object[] { action });
                GD.Print($"[SoulLink][VGQ] Successfully enqueued {action.GetType().Name} via runManager.Queue()");
                return;
            }
        }
        catch { }

        // TEMPORARY: Use reflection to discover the correct queueing method
        // Discovery of RunManager API surface for queueing actions
        var runManagerType = runManager.GetType();

        // Log all public methods that might be related to queueing/actions
        var methods = runManagerType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name.Contains("Queue") || m.Name.Contains("Action") || m.Name.Contains("Enqueue") || m.Name.Contains("Add"))
            .ToList();

        if (methods.Count > 0)
        {
            GD.Print($"[SoulLink][VGQ] Found {methods.Count} potential queueing methods on RunManager:");
            foreach (var method in methods)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                GD.Print($"[SoulLink][VGQ]   - {method.ReturnType.Name} {method.Name}({parameters})");
            }
        }
        else
        {
            GD.PrintErr("[SoulLink][VGQ] No methods found matching Queue/Action/Enqueue/Add pattern on RunManager");

            // Log all public properties that might give access to an action queue
            var properties = runManagerType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.Name.Contains("Queue") || p.Name.Contains("Action"))
                .ToList();

            if (properties.Count > 0)
            {
                GD.Print($"[SoulLink][VGQ] Found {properties.Count} potential action queue properties:");
                foreach (var prop in properties)
                {
                    GD.Print($"[SoulLink][VGQ]   - {prop.PropertyType.Name} {prop.Name}");
                }
            }
        }

        GD.PrintErr($"[SoulLink][VGQ] Cannot enqueue {action.GetType().Name} - queueing method not yet discovered");
    }

    /// <summary>
    /// Convenience method for enqueueing HP change actions.
    /// </summary>
    public static void RequestEnqueueHpChange(int deltaHp, int playerSlot, bool inCombat, string? source = null)
    {
        RequestEnqueue(new SoulLinkHpChangeGameAction(deltaHp, playerSlot, inCombat, source));
    }

    /// <summary>
    /// Convenience method for enqueueing MaxHP change actions.
    /// </summary>
    public static void RequestEnqueueMaxHpChange(int deltaMaxHp, int playerSlot, bool inCombat, string? source = null)
    {
        RequestEnqueue(new SoulLinkMaxHpChangeGameAction(deltaMaxHp, playerSlot, inCombat, source));
    }

    /// <summary>
    /// Convenience method for enqueueing Gold change actions.
    /// </summary>
    public static void RequestEnqueueGoldChange(int deltaGold, int playerSlot, GoldSharingMode mode, string? source = null)
    {
        RequestEnqueue(new SoulLinkGoldChangeGameAction(deltaGold, playerSlot, mode, source));
    }
}
