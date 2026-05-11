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
/// VGQ (Vanilla GameAction Queue) implementation for Soul Link Gold synchronization.
///
/// This GameAction subclass integrates with STS2's native action queue to provide
/// deterministic ordering guarantees for Gold changes across multiplayer peers.
///
/// Key differences from MNA GoldChangeAction:
/// - Integrated with vanilla action queue (not separate pipeline)
/// - Uses ToNetAction()/ToGameAction() for network serialization
/// - Relies on queue ordering instead of deduplication dictionaries
/// </summary>
public class SoulLinkGoldChangeGameAction : GameAction
{
    public int DeltaGold { get; private set; }
    public int PlayerSlot { get; private set; }
    public GoldSharingMode Mode { get; private set; }
    public string? Source { get; private set; }

    /// <summary>
    /// Constructor for local action creation (host enqueueing the action).
    /// </summary>
    public SoulLinkGoldChangeGameAction(int deltaGold, int playerSlot, GoldSharingMode mode, string? source = null)
    {
        DeltaGold = deltaGold;
        PlayerSlot = playerSlot;
        Mode = mode;
        Source = source;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public SoulLinkGoldChangeGameAction()
    {
    }

    public override GameActionType ActionType => GameActionType.NonCombat;

    public override ulong OwnerId => 0; // System-owned action (not tied to a specific player)

    public override MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction ToNetAction()
    {
        return new SoulLinkGoldChangeNetAction
        {
            DeltaGold = this.DeltaGold,
            PlayerSlot = this.PlayerSlot,
            Mode = this.Mode,
            Source = this.Source
        };
    }

    /// <summary>
    /// Execute the Gold change deterministically on all peers.
    /// This is called by the action queue when the action is processed.
    /// </summary>
    protected override async System.Threading.Tasks.Task ExecuteAction()
    {
        await System.Threading.Tasks.Task.CompletedTask;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            GD.PrintErr("[SoulLink][VGQ] SoulLinkGoldChangeGameAction executed but UseVGQSync is disabled");
            return;
        }

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing))
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
        GD.Print($"[SoulLink][VGQ] Apply Gold: slot={PlayerSlot} delta={DeltaGold} mode={Mode} poolBefore={SoulLinkSession.Gold}{sourceStr}");

        // Apply the delta to the shared or split gold pool based on mode
        int canonicalGold = 0;
        int playerCount = runState.Players.Count;

        if (Mode == GoldSharingMode.SharedPool)
        {
            // SharedPool: all players share one gold pool
            canonicalGold = SoulLinkSession.ApplyGoldDelta(DeltaGold, playerCount, PlayerSlot, false, null, Source);

            // Write canonical gold to all player gold fields
            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                foreach (var player in runState.Players)
                {
                    player.Gold = canonicalGold;
                }
            }
            finally
            {
                SoulLinkMod.ApplyingCanonical = false;
            }
        }
        else if (Mode == GoldSharingMode.SplitByPlayer)
        {
            // SplitByPlayer: each player has their own gold, but gains are split
            // This is a more complex mode that requires per-player gold tracking
            canonicalGold = SoulLinkSession.ApplyGoldDelta(DeltaGold, playerCount, PlayerSlot, false, null, Source);

            SoulLinkMod.ApplyingCanonical = true;
            try
            {
                // Write per-player gold values
                for (int i = 0; i < runState.Players.Count; i++)
                {
                    runState.Players[i].Gold = SoulLinkSession.GetPlayerGold(i);
                }
            }
            finally
            {
                SoulLinkMod.ApplyingCanonical = false;
            }
        }

        // Refresh UI panels
        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}

/// <summary>
/// Network representation of SoulLinkGoldChangeGameAction for serialization.
/// </summary>
public struct SoulLinkGoldChangeNetAction : MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction
{
    public int DeltaGold { get; set; }
    public int PlayerSlot { get; set; }
    public GoldSharingMode Mode { get; set; }
    public string? Source { get; set; }

    public GameAction ToGameAction(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        return new SoulLinkGoldChangeGameAction(DeltaGold, PlayerSlot, Mode, Source);
    }

    public void Serialize(MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter writer)
    {
        writer.WriteInt(DeltaGold, 32);
        writer.WriteInt(PlayerSlot, 8);
        writer.WriteEnum(Mode);
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
        DeltaGold = reader.ReadInt(32);
        PlayerSlot = reader.ReadInt(8);
        Mode = reader.ReadEnum<GoldSharingMode>();
        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;
    }
}
