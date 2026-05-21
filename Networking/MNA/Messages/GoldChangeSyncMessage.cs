using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod.Messages;

public struct GoldChangeSyncMessage : INetMessage, IPacketSerializable
{
    public int DeltaGold;
    public int PlayerSlot;
    public string? Source;
    public bool WasBlocked;
    public long Timestamp;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaGold);
        writer.WriteInt(PlayerSlot);
        writer.WriteBool(WasBlocked);
        writer.WriteLong(Timestamp);

        bool hasSource = Source != null;
        writer.WriteBool(hasSource);
        if (hasSource)
            writer.WriteString(Source!);
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaGold = reader.ReadInt();
        PlayerSlot = reader.ReadInt();
        WasBlocked = reader.ReadBool();
        Timestamp = reader.ReadLong();

        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
