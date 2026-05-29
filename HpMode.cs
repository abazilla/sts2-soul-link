namespace SoulLinkMod;

/// <summary>
/// Controls how HP and MaxHp sync behaves for a Soul Link run.
/// Set by the host before the run and broadcast to all clients at run start.
/// Gold semantics are independent — see <see cref="GoldSharingMode"/>.
/// </summary>
public enum HpMode
{
    /// <summary>
    /// Shared HP pool across all players. <see cref="StartingHpMode"/> picks
    /// average vs additive starting HP for the pool.
    /// </summary>
    SharedPool = 0,

    /// <summary>
    /// Per-player HP — STS2 native multiplayer behaviour. SharedLoseHp is ignored.
    /// </summary>
    Vanilla = 1,

    /// <summary>
    /// Shared HP pool AND shared combat block pool. HP semantics match
    /// <see cref="SharedPool"/>; additionally, block gained by any peer goes into a
    /// single canonical pool (<c>SoulLinkSession.SharedBlock</c>) mirrored to every
    /// peer's <c>creature.Block</c>. Coupled — shared block requires shared HP.
    /// See ADR-0002.
    /// </summary>
    SharedHpAndBlock = 2,
}
