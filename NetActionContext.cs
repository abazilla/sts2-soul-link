using System;

namespace SoulLinkMod;

/// <summary>
/// Default implementation of INetActionContext.
/// Provides runtime context information to INetAction.Execute().
/// </summary>
public class NetActionContext : INetActionContext
{
    public int OriginatingPlayerSlot { get; init; }
    public bool IsLocal { get; init; }
    public long Timestamp { get; init; }

    /// <summary>
    /// Creates a new action context for a local action.
    /// </summary>
    public static NetActionContext CreateLocal(int playerSlot)
    {
        return new NetActionContext
        {
            OriginatingPlayerSlot = playerSlot,
            IsLocal = true,
            Timestamp = GetTimestamp(),
        };
    }

    /// <summary>
    /// Creates a new action context for a remote action.
    /// </summary>
    public static NetActionContext CreateRemote(int playerSlot, long timestamp)
    {
        return new NetActionContext
        {
            OriginatingPlayerSlot = playerSlot,
            IsLocal = false,
            Timestamp = timestamp,
        };
    }

    /// <summary>
    /// Gets current timestamp in milliseconds since epoch.
    /// Used for action ordering and deduplication.
    /// </summary>
    private static long GetTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
