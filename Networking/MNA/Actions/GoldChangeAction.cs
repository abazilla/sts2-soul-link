using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.Actions;

public struct GoldChangeAction : INetAction
{
    public int DeltaGold;
    public int PlayerSlot;
    public string? Source;
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
            SoulLinkLog.Error($"[GoldChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[GoldChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string blockedStr = WasBlocked ? " [BLOCKED]" : "";
        SoulLinkLog.Debug($"[GoldChangeAction] Execute: player={PlayerSlot} delta={DeltaGold}{sourceStr}{blockedStr} isLocal={context.IsLocal}");

        if (WasBlocked)
            return;

        // On the local machine, the GoldSyncPatch already applied the delta and mirrored to all players.
        // Only remote machines need to apply the delta through this action.
        if (context.IsLocal)
        {
            SoulLinkLog.Debug($"[GoldChangeAction] Skipping local execution (already applied by patch)");
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
