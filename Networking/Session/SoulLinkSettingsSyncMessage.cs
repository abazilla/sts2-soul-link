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
    /// <summary>Cast of <see cref="GoldSharingMode"/> — serialized as int for wire format.</summary>
    public int GoldMode;
    public bool SharedLoseHp;
    /// <summary>Cast of <see cref="HpMode"/> — serialized as int for wire format. Defaults to SharedPool if absent (old peer).</summary>
    public int HpMode;

    // Host -> all clients. Must be true so multi-client lobbies receive the sync on every peer,
    // not just one. Receiver guards against self-apply when running on the host.
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(SplitMaxHp);
        writer.WriteBool(SplitHeal);
        writer.WriteInt(GoldMode);
        writer.WriteBool(SharedLoseHp);
        writer.WriteInt(HpMode);
    }

    public void Deserialize(PacketReader reader)
    {
        SplitMaxHp   = reader.ReadBool();
        SplitHeal    = reader.ReadBool();
        GoldMode     = reader.ReadInt();
        SharedLoseHp = reader.ReadBool();
        // HpMode added after initial release; default to SharedPool (0) if the stream ends early.
        try { HpMode = reader.ReadInt(); }
        catch { HpMode = 0; }
    }
}
