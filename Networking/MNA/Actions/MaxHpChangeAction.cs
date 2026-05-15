using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.Actions;

public struct MaxHpChangeAction : INetAction
{
    public int DeltaMaxHp;
    public int PlayerSlot;
    public bool InCombat;
    public string? Source;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaMaxHp);
        writer.WriteInt(PlayerSlot);
        writer.WriteBool(InCombat);

        bool hasSource = Source != null;
        writer.WriteBool(hasSource);
        if (hasSource)
        {
            writer.WriteString(Source!);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaMaxHp = reader.ReadInt();
        PlayerSlot = reader.ReadInt();
        InCombat = reader.ReadBool();

        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }

    public void Execute(INetActionContext context)
    {
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
            return;

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SharedHealthPool))
            return;

        // On the local machine, the MaxHpSyncPatch already applied the delta via SyncCoordinator.
        if (context.IsLocal)
        {
            SoulLinkLog.Debug($"[MaxHpChangeAction] Skipping local execution (already applied by patch)");
            return;
        }

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            SoulLinkLog.Error($"[MaxHpChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[MaxHpChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        SoulLinkLog.Debug($"[MaxHpChangeAction] Execute: player={PlayerSlot} delta={DeltaMaxHp}{sourceStr}{combatStr} isLocal={context.IsLocal}");

        int playerCount = runState.Players.Count;

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup).
        // Returns false if the local deterministic patch already applied this change.
        bool applied = SyncCoordinator.TryApplyMaxHpDelta(PlayerSlot, DeltaMaxHp, isFromNetwork: true, InCombat, playerCount, Source);
        if (!applied)
        {
            SoulLinkLog.Debug($"[MaxHpChangeAction] Skipping: already applied by local deterministic event");
            return;
        }

        // Write canonical to all creatures
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetMaxHp(SoulLinkSession.MaxHp);
                player.Creature.SetCurrentHp(SoulLinkSession.CurrentHp);
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
