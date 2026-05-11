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

namespace SoulLinkMod.VGQ;

/// <summary>
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link HP synchronization.
///
/// This GameAction subclass integrates with STS2's native action queue to provide
/// deterministic ordering guarantees for HP changes across multiplayer peers.
///
/// Key differences from MNA HpChangeAction:
/// - Integrated with vanilla action queue (not separate pipeline)
/// - Uses ToNetAction()/ToGameAction() for network serialization
/// - Relies on queue ordering instead of deduplication dictionaries
/// </summary>
public class SoulLinkHpChangeGameAction : GameAction
{
    public int DeltaHp { get; private set; }
    public int PlayerSlot { get; private set; }
    public bool InCombat { get; private set; }
    public string? Source { get; private set; }

    /// <summary>
    /// Constructor for local action creation (host enqueueing the action).
    /// </summary>
    public SoulLinkHpChangeGameAction(int deltaHp, int playerSlot, bool inCombat, string? source = null)
    {
        DeltaHp = deltaHp;
        PlayerSlot = playerSlot;
        InCombat = inCombat;
        Source = source;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public SoulLinkHpChangeGameAction()
    {
    }

    public override GameActionType ActionType => InCombat ? GameActionType.Combat : GameActionType.NonCombat;

    public override ulong OwnerId => 0; // System-owned action (not tied to a specific player)

    public override MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction ToNetAction()
    {
        return new SoulLinkHpChangeNetAction
        {
            DeltaHp = this.DeltaHp,
            PlayerSlot = this.PlayerSlot,
            InCombat = this.InCombat,
            Source = this.Source
        };
    }

    /// <summary>
    /// Execute the HP change deterministically on all peers.
    /// This is called by the action queue when the action is processed.
    /// </summary>
    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            GD.PrintErr("[SoulLink][VGQ] SoulLinkHpChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SharedHealthPool))
            return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][VGQ] Apply failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][VGQ] Apply failed: invalid player slot {PlayerSlot}");
            return;
        }

        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string combatStr = InCombat ? " [IN COMBAT]" : "";
        GD.Print($"[SoulLink][VGQ] Apply: slot={PlayerSlot} delta={DeltaHp} poolBefore={SoulLinkSession.CurrentHp}{sourceStr}{combatStr}");

        int playerCount = runState.Players.Count;

        // Re-evaluate InCombat flag at execution time (not message send time)
        bool actualInCombat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false;
        if (actualInCombat != InCombat)
        {
            GD.Print($"[SoulLink][VGQ] InCombat flag mismatch: action={InCombat}, actual={actualInCombat}. Using actual.");
        }

        // Apply the delta to the shared HP pool
        int canonical = SoulLinkSession.ApplyHpDelta(DeltaHp, actualInCombat, playerCount, PlayerSlot, Source);

        // Write canonical HP to all player creatures
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetCurrentHp(canonical);
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
/// Network representation of SoulLinkHpChangeGameAction for serialization.
/// </summary>
public struct SoulLinkHpChangeNetAction : MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction
{
    public int DeltaHp { get; set; }
    public int PlayerSlot { get; set; }
    public bool InCombat { get; set; }
    public string? Source { get; set; }

    public GameAction ToGameAction(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        return new SoulLinkHpChangeGameAction(DeltaHp, PlayerSlot, InCombat, Source);
    }

    public void Serialize(MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter writer)
    {
        writer.WriteInt(DeltaHp, 32);
        writer.WriteInt(PlayerSlot, 8);
        writer.WriteBool(InCombat);
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
        DeltaHp = reader.ReadInt(32);
        PlayerSlot = reader.ReadInt(8);
        InCombat = reader.ReadBool();
        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
