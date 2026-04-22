using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

/// <summary>
/// Broadcast by the host at run start so all clients adopt the host's run settings.
/// Received by SettingsSyncHandler in RunStartPatch.cs, which writes the values
/// into SoulLinkSession.ActiveRunSettings.
///
/// STS2 auto-registers this via ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;().
/// </summary>
public struct SoulLinkSettingsSyncMessage : INetMessage, IPacketSerializable
{
    public bool SplitMaxHp;
    public bool SplitHeal;
    public bool ShareGold;
    public bool SplitGold;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(SplitMaxHp);
        writer.WriteBool(SplitHeal);
        writer.WriteBool(ShareGold);
        writer.WriteBool(SplitGold);
    }

    public void Deserialize(PacketReader reader)
    {
        SplitMaxHp = reader.ReadBool();
        SplitHeal  = reader.ReadBool();
        ShareGold  = reader.ReadBool();
        SplitGold  = reader.ReadBool();
    }
}
