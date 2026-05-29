using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.VGQ;

/// <summary>
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link shared combat block.
/// Carries a per-peer block delta into the action queue; ExecuteAction applies the
/// delta to <see cref="SoulLinkSession.SharedBlock"/> and mirrors the canonical to
/// every peer's <c>creature.Block</c>. See ADR-0002.
/// </summary>
public class SoulLinkBlockChangeGameAction : GameAction
{
    public int DeltaBlock { get; private set; }
    public int PlayerSlot { get; private set; }
    public string? Source { get; private set; }

    public SoulLinkBlockChangeGameAction(int deltaBlock, int playerSlot, string? source = null)
    {
        DeltaBlock = deltaBlock;
        PlayerSlot = playerSlot;
        Source = source;
        _ownerId = ResolveOwnerId(playerSlot);
    }

    public SoulLinkBlockChangeGameAction() { }

    private static ulong ResolveOwnerId(int playerSlot)
    {
        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs != null && playerSlot >= 0 && playerSlot < rs.Players.Count)
            return rs.Players[playerSlot].NetId;
        return LocalContext.NetId ?? 0;
    }

    // Block changes are combat-only. Use vanilla IsInProgress so the action type
    // matches the queue's current phase on every peer.
    public override GameActionType ActionType
        => CombatManager.Instance?.IsInProgress == true
            ? GameActionType.Combat
            : GameActionType.NonCombat;

    private ulong _ownerId;
    public void SetOwnerId(ulong id) => _ownerId = id;
    public override ulong OwnerId => _ownerId;

    public override MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction ToNetAction()
    {
        return new SoulLinkBlockChangeNetAction
        {
            DeltaBlock = this.DeltaBlock,
            PlayerSlot = this.PlayerSlot,
            Source = this.Source
        };
    }

    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            SoulLinkLog.Error("[VGQ] SoulLinkBlockChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (!SoulLinkSession.IsSharedBlockEnabled) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            SoulLinkLog.Error($"[VGQ] Apply Block failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[VGQ] Apply Block failed: invalid player slot {PlayerSlot}");
            return;
        }

        int canonical = SoulLinkSession.ApplyBlockDelta(DeltaBlock, PlayerSlot, Source);
        SoulLinkLog.Debug($"[VGQ] Apply Block: slot={PlayerSlot} delta={DeltaBlock} canonical={canonical}");

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetBlock(canonical);
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

public struct SoulLinkBlockChangeNetAction : MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction
{
    public int DeltaBlock { get; set; }
    public int PlayerSlot { get; set; }
    public string? Source { get; set; }

    public GameAction ToGameAction(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        return new SoulLinkBlockChangeGameAction(DeltaBlock, PlayerSlot, Source);
    }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaBlock, 32);
        writer.WriteInt(PlayerSlot, 8);
        if (Source != null)
        {
            writer.WriteBool(true);
            writer.WriteString(Source);
        }
        else
        {
            writer.WriteBool(false);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaBlock = reader.ReadInt(32);
        PlayerSlot = reader.ReadInt(8);
        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
