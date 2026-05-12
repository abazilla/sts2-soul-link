namespace SoulLinkMod;

/// <summary>
/// Controls how HP and MaxHp sync behaves for a Soul Link run.
/// Set by the host before the run and broadcast to all clients at run start.
/// Gold semantics are independent — see <see cref="GoldSharingMode"/>.
/// </summary>
public enum HpMode
{
    /// <summary>
    /// Shared HP pool across all players. SplitMaxHp and SplitHeal are respected.
    /// </summary>
    SharedPool = 0,

    /// <summary>
    /// Per-player HP — STS2 native multiplayer behaviour. SplitMaxHp, SplitHeal, and
    /// SharedLoseHp are ignored.
    /// </summary>
    Vanilla = 1,

    /// <summary>
    /// Not yet implemented. Reserved for a future mode where each player has a
    /// separate HP bar but block is shared. Do not use at runtime.
    /// </summary>
    SharedBlockSharedPool = 2,
}
