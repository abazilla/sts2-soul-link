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
/// receiving peer can apply the canonical value and log the change.
/// Also sent once at run start (from RunStartPatch) to handle Neow bonuses
/// that fire before IsActive is set.
/// </summary>
public struct SoulLinkGoldSyncMessage : INetMessage, IPacketSerializable
{
    public int CanonicalGold;
    /// <summary>Signed gold change (positive = gained, negative = spent). 0 on initial sync.</summary>
    public int Delta;
    /// <summary>Slot index of the player whose gold changed.</summary>
    public int PlayerSlot;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(CanonicalGold);
        writer.WriteInt(Delta);
        writer.WriteInt(PlayerSlot);
    }

    public void Deserialize(PacketReader reader)
    {
        CanonicalGold = reader.ReadInt();
        Delta         = reader.ReadInt();
        PlayerSlot    = reader.ReadInt();
    }
}
