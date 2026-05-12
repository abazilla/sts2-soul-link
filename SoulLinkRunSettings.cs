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
    /// </summary>
    public bool SplitMaxHp;

    /// <summary>
    /// When true, out-of-combat CurrentHP heals are divided by the number of
    /// players before being applied to the shared pool.
    /// </summary>
    public bool SplitHeal;

    /// <summary>How gold is managed between players. Independent of <see cref="HpMode"/>.</summary>
    public GoldSharingMode GoldMode;

    /// <summary>
    /// When true, "lose HP" relics and cards trigger for teammates too when a
    /// teammate takes unblocked damage.
    /// </summary>
    public bool SharedLoseHp;

    /// <summary>
    /// Controls HP/MaxHp sync for this run. When Vanilla, HP/MaxHp sync is suppressed
    /// and STS2 handles HP natively. Gold is unaffected by this setting.
    /// Defaults to SharedPool to preserve existing behaviour on old saves.
    /// </summary>
    public HpMode HpMode;

    /// <summary>Returns a SoulLinkRunSettings with the recommended defaults.</summary>
    public static SoulLinkRunSettings Default => new SoulLinkRunSettings
    {
        SplitMaxHp   = true,
        SplitHeal    = true,
        GoldMode     = GoldSharingMode.Default,
        SharedLoseHp = false,
        HpMode       = HpMode.SharedPool,
    };
}
