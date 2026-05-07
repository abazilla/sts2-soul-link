using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

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

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][HpChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][HpChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        GD.Print($"[SoulLink][HpChangeAction] Execute: player={PlayerSlot} delta={DeltaHp}{sourceStr}{combatStr} isLocal={context.IsLocal}");

        // On the local machine, the HpSyncPatch already applied the delta.
        // Only remote machines need to apply the delta through this action.
        if (context.IsLocal)
        {
            GD.Print($"[SoulLink][HpChangeAction] Skipping local execution (already applied by patch)");
            return;
        }

        int playerCount = runState.Players.Count;

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            int canonical = SoulLinkSession.ApplyHpDelta(DeltaHp, InCombat, playerCount, PlayerSlot, Source);

            for (int i = 0; i < runState.Players.Count; i++)
            {
                if (i != PlayerSlot)
                {
                    runState.Players[i].Creature.SetCurrentHp(canonical);
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
