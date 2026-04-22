namespace SoulLinkMod;

/// <summary>
/// The subset of Soul Link settings that govern shared-pool behaviour for a run.
/// These are set by the host and broadcast to all clients at run start.
/// Once a run is active, these values are locked — changes to SoulLinkSettings
/// do not affect an in-progress run.
/// </summary>
public struct SoulLinkRunSettings
{
    /// <summary>
    /// When true, out-of-combat MaxHP changes (gains AND losses) are divided by
    /// the number of players before being applied to the shared pool.
    /// When false, the full amount is applied.
    /// Default: true.
    /// </summary>
    public bool SplitMaxHp;

    /// <summary>
    /// When true, out-of-combat CurrentHP heals are divided by the number of
    /// players before being applied to the shared pool.
    /// When false, the full heal amount is applied.
    /// Default: true.
    /// </summary>
    public bool SplitHeal;

    /// <summary>
    /// When true, all players share a single gold pool (current default behaviour).
    /// When false, each player keeps their own gold independently and GoldSyncPatch
    /// is skipped entirely.
    /// Default: true.
    /// </summary>
    public bool ShareGold;

    /// <summary>
    /// Only relevant when ShareGold is true.
    /// When true, gold changes are divided by the number of players before being
    /// applied to the shared pool.
    /// When false, the full delta is applied.
    /// Default: false.
    /// </summary>
    public bool SplitGold;

    /// <summary>Returns a SoulLinkRunSettings with the recommended defaults.</summary>
    public static SoulLinkRunSettings Default => new SoulLinkRunSettings
    {
        SplitMaxHp = true,
        SplitHeal  = true,
        ShareGold  = true,
        SplitGold  = false,
    };
}
