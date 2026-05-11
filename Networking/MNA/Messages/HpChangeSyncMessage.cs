using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod.Messages;

public struct HpChangeSyncMessage : INetMessage, IPacketSerializable
{
    public int DeltaHp;
    public int PlayerSlot;
    public bool InCombat;
    public string? Source;
    public long Timestamp;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaHp);
        writer.WriteInt(PlayerSlot);
        writer.WriteBool(InCombat);
        writer.WriteLong(Timestamp);

        bool hasSource = Source != null;
        writer.WriteBool(hasSource);
        if (hasSource)
            writer.WriteString(Source!);
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaHp = reader.ReadInt();
        PlayerSlot = reader.ReadInt();
        InCombat = reader.ReadBool();
        Timestamp = reader.ReadLong();

        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
