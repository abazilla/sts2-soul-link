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
    /// <summary>Cast of <see cref="GoldSharingMode"/> — serialized as int for wire format.</summary>
    public int GoldMode;
    public bool SharedLoseHp;
    /// <summary>Cast of <see cref="HpMode"/> — serialized as int for wire format. Defaults to SharedPool if absent (old peer).</summary>
    public int HpMode;
    /// <summary>Cast of <see cref="StartingHpMode"/> — serialized as int. Defaults to Average if absent (old peer).</summary>
    public int StartingHpMode;

    // Host -> all clients. Must be true so multi-client lobbies receive the sync on every peer,
    // not just one. Receiver guards against self-apply when running on the host.
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(GoldMode);
        writer.WriteBool(SharedLoseHp);
        writer.WriteInt(HpMode);
        writer.WriteInt(StartingHpMode);
    }

    public void Deserialize(PacketReader reader)
    {
        GoldMode     = reader.ReadInt();
        SharedLoseHp = reader.ReadBool();
        // HpMode added after initial release; default to SharedPool (0) if the stream ends early.
        try { HpMode = reader.ReadInt(); }
        catch { HpMode = 0; }
        // StartingHpMode added after HpMode; default to Average (0) if absent.
        try { StartingHpMode = reader.ReadInt(); }
        catch { StartingHpMode = 0; }
    }
}
