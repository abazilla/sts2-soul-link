namespace SoulLinkMod;

/// <summary>
/// Controls how gold is managed between players in a Soul Link run.
/// Set by the host before the run and broadcast to all clients at run start.
/// </summary>
public enum GoldSharingMode
{
    /// <summary>
    /// STS2 native behaviour. Each player has their own independent gold pool.
    /// Soul Link does not intercept any gold changes.
    /// </summary>
    Default = 0,

    /// <summary>
    /// One combined pool shared between all players.
    /// Gold gains go to the pool; gold spends come from the pool.
    /// All players see the same total.
    /// </summary>
    SharedPool = 1,

    /// <summary>
    /// Each player has their own pool, but gold gains are divided by player count
    /// so the windfall is shared proportionally.
    /// Spending comes only from the buyer's own pool — other players are unaffected.
    /// </summary>
    SplitByPlayer = 2,
}
