using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

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

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][MaxHpChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][MaxHpChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        GD.Print($"[SoulLink][MaxHpChangeAction] Execute: player={PlayerSlot} delta={DeltaMaxHp}{sourceStr}{combatStr} isLocal={context.IsLocal}");

        // On the local machine, the MaxHpSyncPatch already applied the delta.
        // Only remote machines need to apply the delta through this action.
        if (context.IsLocal)
        {
            GD.Print($"[SoulLink][MaxHpChangeAction] Skipping local execution (already applied by patch)");
            return;
        }

        int playerCount = runState.Players.Count;

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            SoulLinkSession.ApplyMaxHpDelta(DeltaMaxHp, InCombat, playerCount, PlayerSlot, Source);

            for (int i = 0; i < runState.Players.Count; i++)
            {
                if (i != PlayerSlot)
                {
                    runState.Players[i].Creature.SetMaxHp(SoulLinkSession.MaxHp);
                    runState.Players[i].Creature.SetCurrentHp(SoulLinkSession.CurrentHp);
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
