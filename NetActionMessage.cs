using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

/// <summary>
/// Generic wrapper message for transporting INetAction across the network.
///
/// Each INetAction type gets its own message instance with the full action data.
/// The action is fully serialized/deserialized through IPacketSerializable.
/// </summary>
public struct NetActionMessage<T> : INetMessage, IPacketSerializable where T : struct, INetAction
{
    /// <summary>The action being transported.</summary>
    public T Action;

    /// <summary>Timestamp when the action was created (for ordering).</summary>
    public long Timestamp;

    /// <summary>The player slot that originated this action.</summary>
    public int PlayerSlot;

    public bool ShouldBroadcast => Action.ShouldBroadcast;
    public NetTransferMode Mode => Action.Mode;
    public LogLevel LogLevel => Action.LogLevel;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteLong(Timestamp);
        writer.WriteInt(PlayerSlot);
        Action.Serialize(writer);
    }

    public void Deserialize(PacketReader reader)
    {
        Timestamp = reader.ReadLong();
        PlayerSlot = reader.ReadInt();
        Action.Deserialize(reader);
    }
}
