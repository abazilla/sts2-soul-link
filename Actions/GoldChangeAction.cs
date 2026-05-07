using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.Actions;

/// <summary>
/// INetAction implementation for synchronized gold changes.
///
/// Represents a deterministic gold mutation that should be applied on all peers.
/// Includes source tracking for logging and blocked-change tracking for relics.
/// </summary>
public struct GoldChangeAction : INetAction
{
    /// <summary>
    /// Amount of gold to add (positive) or remove (negative).
    /// </summary>
    public int DeltaGold;

    /// <summary>
    /// Player slot that initiated this gold change.
    /// </summary>
    public int PlayerSlot;

    /// <summary>
    /// Source/reason for the gold change (for logging).
    /// Null if unknown or not applicable.
    /// </summary>
    public string? Source;

    /// <summary>
    /// Whether this gold change was blocked (e.g., by Ectoplasm).
    /// If true, Execute() should log but not apply the change.
    /// </summary>
    public bool WasBlocked;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaGold);
        writer.WriteInt(PlayerSlot);

        bool hasSource = Source != null;
        writer.WriteBool(hasSource);
        if (hasSource)
        {
            writer.WriteString(Source!);
        }

        writer.WriteBool(WasBlocked);
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaGold = reader.ReadInt();
        PlayerSlot = reader.ReadInt();

        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;

        WasBlocked = reader.ReadBool();
    }

    public void Execute(INetActionContext context)
    {
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
            return;

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing))
            return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][GoldChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][GoldChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string blockedStr = WasBlocked ? " [BLOCKED]" : "";
        GD.Print($"[SoulLink][GoldChangeAction] Execute: player={PlayerSlot} delta={DeltaGold}{sourceStr}{blockedStr} isLocal={context.IsLocal}");

        if (WasBlocked)
            return;

        // On the local machine, the GoldSyncPatch already applied the delta and mirrored to all players.
        // Only remote machines need to apply the delta through this action.
        if (context.IsLocal)
        {
            GD.Print($"[SoulLink][GoldChangeAction] Skipping local execution (already applied by patch)");
            return;
        }

        int playerCount = runState.Players.Count;
        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            int canonical = SoulLinkSession.ApplyGoldDelta(DeltaGold, playerCount, PlayerSlot, blocked: false, source: Source);

            if (goldMode == GoldSharingMode.SplitByPlayer)
            {
                // In SplitByPlayer, each player shows their own gold
                for (int i = 0; i < runState.Players.Count; i++)
                {
                    runState.Players[i].Gold = SoulLinkSession.GetPlayerGold(i);
                }
            }
            else
            {
                // In SharedPool, all players see the same canonical value
                for (int i = 0; i < runState.Players.Count; i++)
                {
                    runState.Players[i].Gold = canonical;
                }
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}
