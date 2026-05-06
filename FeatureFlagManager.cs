using System;
using System.Collections.Generic;

namespace SoulLinkMod;

/// <summary>
/// Manages feature flags for the Soul Link mod.
///
/// Provides centralized control for enabling/disabling mod features at runtime.
/// Flags are synchronized across all clients in multiplayer sessions.
///
/// Phase 1 implementation: basic flag storage and checking.
/// Future phases will add:
/// - Persistence (save/load from mod settings)
/// - Network synchronization (broadcast flag changes)
/// - Per-player flags (for asymmetric gameplay)
/// - Runtime toggling via console commands
/// </summary>
public static class FeatureFlagManager
{
    /// <summary>
    /// Global flag state (persisted across runs).
    /// </summary>
    private static readonly Dictionary<FeatureFlag, bool> _globalFlags = new();

    /// <summary>
    /// Run-scoped flag state (reset on new run).
    /// </summary>
    private static readonly Dictionary<FeatureFlag, bool> _runFlags = new();

    /// <summary>
    /// Session-scoped flag state (reset on game restart).
    /// </summary>
    private static readonly Dictionary<FeatureFlag, bool> _sessionFlags = new();

    /// <summary>
    /// Default flag values and their scopes.
    /// Used to initialize flags on first access and for reset operations.
    /// </summary>
    private static readonly Dictionary<FeatureFlag, (bool defaultValue, FeatureFlagScope scope)> _defaults = new()
    {
        [FeatureFlag.SoulLinkEnabled] = (true, FeatureFlagScope.Global),
        [FeatureFlag.SharedHealthPool] = (true, FeatureFlagScope.Run),
        [FeatureFlag.GoldSharing] = (true, FeatureFlagScope.Run),
        [FeatureFlag.NetworkedActions] = (false, FeatureFlagScope.Session), // Phase 1: disabled by default
        [FeatureFlag.DebugOverlay] = (true, FeatureFlagScope.Session),
        [FeatureFlag.CombatLog] = (true, FeatureFlagScope.Session),
        [FeatureFlag.RunStatsPanel] = (true, FeatureFlagScope.Session),
        [FeatureFlag.VerboseNetworkLogging] = (false, FeatureFlagScope.Session),
    };

    /// <summary>
    /// Checks if a feature flag is enabled.
    /// </summary>
    /// <param name="flag">The feature flag to check</param>
    /// <returns>True if enabled, false otherwise</returns>
    public static bool IsEnabled(FeatureFlag flag)
    {
        var (defaultValue, scope) = GetDefaultAndScope(flag);

        return scope switch
        {
            FeatureFlagScope.Global => _globalFlags.GetValueOrDefault(flag, defaultValue),
            FeatureFlagScope.Run => _runFlags.GetValueOrDefault(flag, defaultValue),
            FeatureFlagScope.Session => _sessionFlags.GetValueOrDefault(flag, defaultValue),
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Sets a feature flag to the specified value.
    /// </summary>
    /// <param name="flag">The feature flag to set</param>
    /// <param name="enabled">Whether the flag should be enabled</param>
    public static void SetFlag(FeatureFlag flag, bool enabled)
    {
        var (_, scope) = GetDefaultAndScope(flag);

        switch (scope)
        {
            case FeatureFlagScope.Global:
                _globalFlags[flag] = enabled;
                // TODO: persist to mod settings file
                break;
            case FeatureFlagScope.Run:
                _runFlags[flag] = enabled;
                // TODO: broadcast to all clients if in multiplayer
                break;
            case FeatureFlagScope.Session:
                _sessionFlags[flag] = enabled;
                break;
        }
    }

    /// <summary>
    /// Resets all run-scoped flags to their defaults.
    /// Called when a new run starts.
    /// </summary>
    public static void ResetRunFlags()
    {
        _runFlags.Clear();
    }

    /// <summary>
    /// Resets all session-scoped flags to their defaults.
    /// Called on mod initialization/game restart.
    /// </summary>
    public static void ResetSessionFlags()
    {
        _sessionFlags.Clear();
    }

    /// <summary>
    /// Resets all flags (all scopes) to their defaults.
    /// Useful for testing or recovering from invalid state.
    /// </summary>
    public static void ResetAllFlags()
    {
        _globalFlags.Clear();
        _runFlags.Clear();
        _sessionFlags.Clear();
    }

    /// <summary>
    /// Gets the default value and scope for a feature flag.
    /// </summary>
    private static (bool defaultValue, FeatureFlagScope scope) GetDefaultAndScope(FeatureFlag flag)
    {
        return _defaults.TryGetValue(flag, out var config)
            ? config
            : (false, FeatureFlagScope.Session); // Safe default: disabled, session-scoped
    }

    /// <summary>
    /// Gets all flags and their current states.
    /// Useful for debugging and settings UI.
    /// </summary>
    public static Dictionary<FeatureFlag, bool> GetAllFlags()
    {
        var result = new Dictionary<FeatureFlag, bool>();
        foreach (FeatureFlag flag in Enum.GetValues<FeatureFlag>())
        {
            result[flag] = IsEnabled(flag);
        }
        return result;
    }

    /// <summary>
    /// Initializes the feature flag system.
    /// Called once on mod load.
    /// </summary>
    internal static void Initialize()
    {
        // Load global flags from settings (Phase 2)
        // For Phase 1, just use defaults
        ResetSessionFlags();
    }
}
