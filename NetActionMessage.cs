using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

public struct NetActionMessage<T> : INetMessage, IPacketSerializable where T : struct, INetAction
{
    public T Action;
    public long Timestamp;
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
