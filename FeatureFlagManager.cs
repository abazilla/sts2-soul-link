using System;
using System.Collections.Generic;

namespace SoulLinkMod;

/// <summary>
/// Manages feature flags for the Soul Link mod.
/// Provides centralized control for enabling/disabling mod features at runtime.
/// </summary>
public static class FeatureFlagManager
{
    private static readonly Dictionary<FeatureFlag, bool> _globalFlags = new();
    private static readonly Dictionary<FeatureFlag, bool> _runFlags = new();
    private static readonly Dictionary<FeatureFlag, bool> _sessionFlags = new();

    private static readonly Dictionary<FeatureFlag, (bool defaultValue, FeatureFlagScope scope)> _defaults = new()
    {
        [FeatureFlag.SoulLinkEnabled] = (true, FeatureFlagScope.Global),
        [FeatureFlag.SharedHealthPool] = (true, FeatureFlagScope.Run),
        [FeatureFlag.GoldSharing] = (true, FeatureFlagScope.Run),
        [FeatureFlag.NetworkedActions] = (false, FeatureFlagScope.Session),
        [FeatureFlag.DebugOverlay] = (true, FeatureFlagScope.Session),
        [FeatureFlag.CombatLog] = (true, FeatureFlagScope.Session),
        [FeatureFlag.RunStatsPanel] = (true, FeatureFlagScope.Session),
        [FeatureFlag.VerboseNetworkLogging] = (false, FeatureFlagScope.Session),
    };

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

    public static void ResetRunFlags() => _runFlags.Clear();

    public static void ResetSessionFlags() => _sessionFlags.Clear();

    public static void ResetAllFlags()
    {
        _globalFlags.Clear();
        _runFlags.Clear();
        _sessionFlags.Clear();
    }

    private static (bool defaultValue, FeatureFlagScope scope) GetDefaultAndScope(FeatureFlag flag)
    {
        return _defaults.TryGetValue(flag, out var config)
            ? config
            : (false, FeatureFlagScope.Session);
    }

    public static Dictionary<FeatureFlag, bool> GetAllFlags()
    {
        var result = new Dictionary<FeatureFlag, bool>();
        foreach (FeatureFlag flag in Enum.GetValues<FeatureFlag>())
        {
            result[flag] = IsEnabled(flag);
        }
        return result;
    }

    internal static void Initialize() => ResetSessionFlags();
}
