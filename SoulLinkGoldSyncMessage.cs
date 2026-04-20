using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

/// <summary>
/// Custom net message that broadcasts the canonical gold value to all peers.
///
/// STS2 scans mod assemblies for INetMessage implementations via
/// ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;() and registers them
/// automatically in MessageTypes — no manual registration needed.
///
/// Sent by GoldSyncPatch whenever the local player's gold changes, so the
/// receiving peer can apply the canonical value to its own local player.
/// Also sent once at run start (from RunStartPatch) to handle Neow bonuses
/// that fire before IsActive is set.
/// </summary>
public struct SoulLinkGoldSyncMessage : INetMessage, IPacketSerializable
{
    public int CanonicalGold;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer) => writer.WriteInt(CanonicalGold);
    public void Deserialize(PacketReader reader) { CanonicalGold = reader.ReadInt(); }
}
