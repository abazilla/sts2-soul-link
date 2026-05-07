using System;

namespace SoulLinkMod;

public class NetActionContext : INetActionContext
{
    public int OriginatingPlayerSlot { get; init; }
    public bool IsLocal { get; init; }
    public long Timestamp { get; init; }

    public static NetActionContext CreateLocal(int playerSlot) => new()
    {
        OriginatingPlayerSlot = playerSlot,
        IsLocal = true,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    public static NetActionContext CreateRemote(int playerSlot, long timestamp) => new()
    {
        OriginatingPlayerSlot = playerSlot,
        IsLocal = false,
        Timestamp = timestamp,
    };
}
