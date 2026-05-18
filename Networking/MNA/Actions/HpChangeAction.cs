using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Patches;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.Actions;

public struct HpChangeAction : INetAction
{
    public int DeltaHp;
    public int PlayerSlot;
    public bool InCombat;
    public string? Source;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaHp);
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
        DeltaHp = reader.ReadInt();
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

        // On the local machine, the HpSyncPatch already applied the delta via SyncCoordinator.
        if (context.IsLocal)
        {
            SoulLinkLog.Debug($"[HpChangeAction] Skipping local execution (already applied by patch)");
            return;
        }

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            SoulLinkLog.Error($"[HpChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[HpChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        SoulLinkLog.Debug($"[HpAction.Execute] slot={PlayerSlot} delta={DeltaHp} isLocal={context.IsLocal} poolBefore={SoulLinkSession.CurrentHp}{sourceStr}{combatStr}");

        int playerCount = runState.Players.Count;

        // Prefer locally-resolved attacker name (in receiver's locale) over the
        // string baked on the sender's machine. Combat state is mirrored across
        // peers; the same monster is mid-move locally, so enemy.Name resolves
        // in this client's language. Falls back to the broadcast string.
        string? localizedSource = Source;
        if (InCombat)
        {
            string? local = HpSyncPatch.ResolveActiveAttacker(runState.Players[PlayerSlot].Creature, InCombat);
            if (!string.IsNullOrEmpty(local)) localizedSource = local;
        }

        // Atomically check-apply-mark via SyncCoordinator (thread-safe dedup).
        // Returns null if the local deterministic patch already applied this change.
        int? result = SyncCoordinator.TryApplyHpDelta(PlayerSlot, DeltaHp, isFromNetwork: true, InCombat, playerCount, localizedSource);
        if (result == null)
        {
            SoulLinkLog.Debug($"[HpChangeAction] Skipping: already applied by local deterministic event");
            return;
        }

        // Write canonical to all creatures
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
                player.Creature.SetCurrentHp(result.Value);
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
