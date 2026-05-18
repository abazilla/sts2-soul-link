namespace SoulLinkMod;

/// <summary>
/// Controls how the shared HP pool's starting MaxHp/CurrentHp is seeded from each
/// player's character. Only meaningful when <see cref="HpMode"/> is SharedPool.
/// </summary>
public enum StartingHpMode
{
    /// <summary>Pool starts at the average of all players' starting HP (current behaviour).</summary>
    Average  = 0,

    /// <summary>Pool starts at the sum of all players' starting HP (easier).</summary>
    Additive = 1,
}
