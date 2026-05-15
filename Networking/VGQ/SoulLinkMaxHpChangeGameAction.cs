using Godot;
using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

using SoulLinkMod;

namespace SoulLinkMod.VGQ;

/// <summary>
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link MaxHP synchronization.
///
/// This GameAction subclass integrates with STS2's native action queue to provide
/// deterministic ordering guarantees for MaxHP changes across multiplayer peers.
///
/// Key differences from MNA MaxHpChangeAction:
/// - Integrated with vanilla action queue (not separate pipeline)
/// - Uses ToNetAction()/ToGameAction() for network serialization
/// - Relies on queue ordering instead of deduplication dictionaries
/// </summary>
public class SoulLinkMaxHpChangeGameAction : GameAction
{
    public int DeltaMaxHp { get; private set; }
    public int PlayerSlot { get; private set; }
    public bool InCombat { get; private set; }
    public int PlayerCount { get; private set; }
    public string? Source { get; private set; }

    /// <summary>
    /// Constructor for local action creation (host enqueueing the action).
    /// </summary>
    public SoulLinkMaxHpChangeGameAction(int deltaMaxHp, int playerSlot, bool inCombat, int playerCount, string? source = null)
    {
        DeltaMaxHp = deltaMaxHp;
        PlayerSlot = playerSlot;
        InCombat = inCombat;
        PlayerCount = playerCount;
        Source = source;
        _ownerId = ResolveOwnerId(playerSlot);
    }

    private static ulong ResolveOwnerId(int playerSlot)
    {
        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs != null && playerSlot >= 0 && playerSlot < rs.Players.Count)
            return rs.Players[playerSlot].NetId;
        return LocalContext.NetId ?? 0;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public SoulLinkMaxHpChangeGameAction()
    {
    }

    public override GameActionType ActionType
        => SoulLinkMod.IsCombatActive() ? GameActionType.Combat : GameActionType.NonCombat;

    private ulong _ownerId;
    public void SetOwnerId(ulong id) => _ownerId = id;
    public override ulong OwnerId => _ownerId;

    public override MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction ToNetAction()
    {
        return new SoulLinkMaxHpChangeNetAction
        {
            DeltaMaxHp = this.DeltaMaxHp,
            PlayerSlot = this.PlayerSlot,
            InCombat = this.InCombat,
            PlayerCount = this.PlayerCount,
            Source = this.Source
        };
    }

    /// <summary>
    /// Execute the MaxHP change deterministically on all peers.
    /// This is called by the action queue when the action is processed.
    /// </summary>
    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            SoulLinkLog.Error("[VGQ] SoulLinkMaxHpChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return;

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SharedHealthPool))
            return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            SoulLinkLog.Error($"[VGQ] Apply failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[VGQ] Apply failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        SoulLinkLog.Debug($"[VGQ] Apply MaxHP: slot={PlayerSlot} delta={DeltaMaxHp} pc={PlayerCount} poolBefore={SoulLinkSession.MaxHp}{sourceStr}{combatStr}");

        // Use the originator's snapshotted PlayerCount and InCombat flag (not local state):
        // peers may execute this action while their RunState.Players differs from the
        // originator's (late joins, drops, or pre-init timing). Re-reading either value
        // locally causes asymmetric scaling — e.g. host scales -12/2=-6 while client
        // scales -12/3=-4 for the same Neow MaxHp loss. The originator's snapshot is
        // authoritative for both apply semantics and queue eligibility.
        SoulLinkSession.ApplyMaxHpDelta(DeltaMaxHp, InCombat, PlayerCount, PlayerSlot, Source);
        int canonicalMaxHp = SoulLinkSession.MaxHp;
        int canonicalHp = SoulLinkSession.CurrentHp;

        // Write canonical MaxHP and CurrentHP to all player creatures
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetMaxHp(canonicalMaxHp);
                player.Creature.SetCurrentHp(canonicalHp);
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Refresh UI panels
        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}

/// <summary>
/// Network representation of SoulLinkMaxHpChangeGameAction for serialization.
/// </summary>
public struct SoulLinkMaxHpChangeNetAction : MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction
{
    public int DeltaMaxHp { get; set; }
    public int PlayerSlot { get; set; }
    public bool InCombat { get; set; }
    public int PlayerCount { get; set; }
    public string? Source { get; set; }

    public GameAction ToGameAction(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        return new SoulLinkMaxHpChangeGameAction(DeltaMaxHp, PlayerSlot, InCombat, PlayerCount, Source);
    }

    public void Serialize(MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter writer)
    {
        writer.WriteInt(DeltaMaxHp, 32);
        writer.WriteInt(PlayerSlot, 8);
        writer.WriteBool(InCombat);
        writer.WriteInt(PlayerCount, 8);
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

    public void Deserialize(MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader reader)
    {
        DeltaMaxHp = reader.ReadInt(32);
        PlayerSlot = reader.ReadInt(8);
        InCombat = reader.ReadBool();
        PlayerCount = reader.ReadInt(8);
        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
