namespace SoulLinkMod;

/// <summary>
/// Feature flags for Soul Link mod functionality.
///
/// Flags can be enabled/disabled at runtime and are synchronized across all clients.
/// Use FeatureFlagManager to check and modify flags.
///
/// Phase 1 scaffold: defines core infrastructure flags.
/// Additional feature-specific flags will be added as needed.
/// </summary>
public enum FeatureFlag
{
    /// <summary>
    /// Master flag: enables all Soul Link functionality.
    /// When false, mod runs in passive mode (hooks installed but not active).
    /// </summary>
    SoulLinkEnabled,

    /// <summary>
    /// Enables shared HP pool mechanic.
    /// When true, all players share a common HP pool.
    /// When false, HP is managed by STS2 default behavior.
    /// </summary>
    SharedHealthPool,

    /// <summary>
    /// Enables gold sharing mechanics (modes: Default, SharedPool, SplitByPlayer).
    /// When false, gold is managed by STS2 default behavior.
    /// </summary>
    GoldSharing,

    /// <summary>
    /// Enables INetAction synchronization system for deterministic multiplayer actions.
    /// When true, custom actions use the INetAction pipeline.
    /// When false, falls back to direct patching and message-based sync.
    /// </summary>
    NetworkedActions,

    /// <summary>
    /// Enables debug overlay UI panel.
    /// Displays real-time sync state, network stats, and diagnostics.
    /// </summary>
    DebugOverlay,

    /// <summary>
    /// Enables combat log panel.
    /// Shows history of HP/gold changes and their sources.
    /// </summary>
    CombatLog,

    /// <summary>
    /// Enables run stats panel.
    /// Displays aggregate run statistics across all players.
    /// </summary>
    RunStatsPanel,

    /// <summary>
    /// Enables verbose logging for multiplayer synchronization.
    /// Useful for debugging desync issues.
    /// </summary>
    VerboseNetworkLogging,
}

/// <summary>
/// Scope at which a feature flag applies.
/// Determines when and how the flag is checked/enforced.
/// </summary>
public enum FeatureFlagScope
{
    /// <summary>
    /// Global scope: flag applies across all runs and sessions.
    /// Persisted in mod settings, survives game restart.
    /// </summary>
    Global,

    /// <summary>
    /// Run scope: flag applies to the current run only.
    /// Reset when a new run starts, synchronized at run creation.
    /// </summary>
    Run,

    /// <summary>
    /// Session scope: flag applies to the current play session only.
    /// Reset on game restart, not persisted.
    /// </summary>
    Session,
}
