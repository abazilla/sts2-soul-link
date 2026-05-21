using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.VGQ;

/// <summary>
/// VGQ action representing Seal of Gold's per-turn effect (lose 5 gold → gain 1 energy).
///
/// Enqueued from <c>SealOfGoldSoulLinkPatch</c> via
/// <see cref="ActionQueueSynchronizer.RequestEnqueue"/>. The vanilla
/// <c>AfterSideTurnStart</c> hook only fires on the relic owner's peer, so per-peer
/// enqueue would leave remote peers without the action; broadcast enqueue puts it
/// on every peer's queue. Gold + energy are applied in a single ExecuteAction so the
/// host does not execute partial effects synchronously inside the hook frame.
/// </summary>
public class SoulLinkSealOfGoldGameAction : GameAction
{
    public int PlayerSlot { get; private set; }
    public GoldSharingMode Mode { get; private set; }
    public int GoldCost { get; private set; }
    public int EnergyGain { get; private set; }

    public SoulLinkSealOfGoldGameAction(int playerSlot, GoldSharingMode mode, int goldCost, int energyGain)
    {
        PlayerSlot = playerSlot;
        Mode = mode;
        GoldCost = goldCost;
        EnergyGain = energyGain;
        _ownerId = ResolveOwnerId(playerSlot);
    }

    public SoulLinkSealOfGoldGameAction() { }

    private static ulong ResolveOwnerId(int playerSlot)
    {
        var rs = RunManager.Instance?.DebugOnlyGetState();
        if (rs != null && playerSlot >= 0 && playerSlot < rs.Players.Count)
            return rs.Players[playerSlot].NetId;
        return LocalContext.NetId ?? 0;
    }

    private ulong _ownerId;
    public override ulong OwnerId => _ownerId;

    public override GameActionType ActionType
        => CombatManager.Instance?.IsInProgress == true
            ? GameActionType.Combat
            : GameActionType.NonCombat;

    public override MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction ToNetAction()
        => new SoulLinkSealOfGoldNetAction
        {
            PlayerSlot = PlayerSlot,
            Mode = Mode,
            GoldCost = GoldCost,
            EnergyGain = EnergyGain,
        };

    protected override async Task ExecuteAction()
    {
        await Task.CompletedTask;

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.UseVGQSync))
        {
            SoulLinkLog.Error("[VGQ] SoulLinkSealOfGoldGameAction executed but UseVGQSync is disabled");
            return;
        }
        // Use action's captured Mode, not local ActiveRunSettings: settings sync may
        // not have completed on remote peers, leaving their GoldMode at Default → action
        // would no-op while originator applied → state diverges.
        if (Mode == GoldSharingMode.Default) return;
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing)) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            SoulLinkLog.Error($"[VGQ][Seal] invalid run state or slot {PlayerSlot}");
            return;
        }

        var owner = runState.Players[PlayerSlot];
        int playerCount = runState.Players.Count;
        const string source = "@relic:SealOfGold";

        SoulLinkLog.Debug($"[VGQ][Seal] slot={PlayerSlot} cost={GoldCost} energy={EnergyGain} poolBefore={SoulLinkSession.Gold}");

        // Visual feedback on every peer (only the owning peer's UI is bound; remote
        // peers' Flash() is a safe no-op). Host-only orchestrator can't Flash for
        // remote relics, so do it here where every peer runs identically.
        var ownedSeal = owner.Relics.OfType<MegaCrit.Sts2.Core.Models.Relics.SealOfGold>().FirstOrDefault();
        ownedSeal?.Flash();

        int canonical = SoulLinkSession.ApplyGoldDelta(-GoldCost, playerCount, PlayerSlot, false, null, source, Mode);

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            if (Mode == GoldSharingMode.SharedPool)
            {
                foreach (var p in runState.Players)
                    p.Gold = canonical;
            }
            else if (Mode == GoldSharingMode.SplitByPlayer)
            {
                for (int i = 0; i < runState.Players.Count; i++)
                    runState.Players[i].Gold = SoulLinkSession.GetPlayerGold(i);
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Energy gain: vanilla helper runs deterministically on every peer.
        await PlayerCmd.GainEnergy(EnergyGain, owner);

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}

public struct SoulLinkSealOfGoldNetAction : MegaCrit.Sts2.Core.GameActions.Multiplayer.INetAction
{
    public int PlayerSlot { get; set; }
    public GoldSharingMode Mode { get; set; }
    public int GoldCost { get; set; }
    public int EnergyGain { get; set; }

    public GameAction ToGameAction(Player player)
        => new SoulLinkSealOfGoldGameAction(PlayerSlot, Mode, GoldCost, EnergyGain);

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(PlayerSlot, 8);
        writer.WriteEnum(Mode);
        writer.WriteInt(GoldCost, 16);
        writer.WriteInt(EnergyGain, 8);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerSlot = reader.ReadInt(8);
        Mode = reader.ReadEnum<GoldSharingMode>();
        GoldCost = reader.ReadInt(16);
        EnergyGain = reader.ReadInt(8);
    }
}
