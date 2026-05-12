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
        // Master kill-switch for the entire mod; when off, hooks stay installed but inert.
        [FeatureFlag.SoulLinkEnabled] = (true, FeatureFlagScope.Global),
        // Shared HP pool across all players; off falls back to vanilla per-player HP.
        [FeatureFlag.SharedHealthPool] = (true, FeatureFlagScope.Run),
        // Gold sharing mechanics (shared pool / split-by-player); off uses vanilla gold.
        [FeatureFlag.GoldSharing] = (true, FeatureFlagScope.Run),
        // In-game debug overlay panel: sync state, net stats, diagnostics.
        [FeatureFlag.DebugOverlay] = (true, FeatureFlagScope.Session),
        // Combat log panel: history of HP/gold changes with sources.
        [FeatureFlag.CombatLog] = (true, FeatureFlagScope.Session),
        // Run stats panel: aggregate stats across all players.
        [FeatureFlag.RunStatsPanel] = (true, FeatureFlagScope.Session),
        // Verbose multiplayer sync logging; noisy, for desync debugging.
        [FeatureFlag.VerboseNetworkLogging] = (false, FeatureFlagScope.Session),

        // MNA (Mod Net Action) pipeline for deterministic multiplayer actions; legacy path, superseded by VGQ when both enabled.
        [FeatureFlag.NetworkedActions] = (false, FeatureFlagScope.Session),
        // VGQ (Vanilla GameAction Queue) sync for HP/MaxHP/Gold; preferred path, takes priority over MNA when both enabled.
        [FeatureFlag.UseVGQSync] = (true, FeatureFlagScope.Session),
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
