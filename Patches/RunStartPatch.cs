using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Hooks RunManager.Launch() to initialize the Soul Link session when a run starts.
/// Hooks RunManager.CleanUp() to tear it down when the run ends.
///
/// RunState.Players is fully populated before Launch() fires — safe to read there.
/// Soul Link only activates for multiplayer runs (Players.Count > 1).
/// </summary>
internal static class GoldSyncHandler
{
    /// <summary>
    /// Receives a canonical gold broadcast from the other peer and applies it locally.
    /// Registered as a message handler when the run starts, unregistered when it ends.
    /// </summary>
    internal static void Handle(SoulLinkGoldSyncMessage message, ulong senderId)
    {
        if (!SoulLinkSession.IsActive) return;
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        SoulLinkSession.SetGoldDirect(message.CanonicalGold);

        // Set canonical gold on ALL players on this machine. STS2 does not sync
        // Player.Gold between machines in real-time; the checksum includes it but
        // nothing broadcasts it automatically. We are responsible for keeping every
        // player object on this machine up to date on every sync message.
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
                player.Gold = message.CanonicalGold;
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Log the change in the kill-feed on the receiving machine.
        // Delta=0 means this is an initial sync (run start), not a real change — skip it.
        SoulLinkSession.LogGoldEntry(message.Delta, message.PlayerSlot,
            SoulLinkSession.CurrentRoomSource);

        CombatLogPanel.Current?.Refresh();
        RunStatsPanel.Current?.Refresh();
        DebugOverlay.Current?.Refresh();
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
public static class RunLaunchPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        GD.Print($"[SoulLink] Run launched with {runState.Players.Count} player(s).");
        SoulLinkSession.OnRunStart();

        if (!SoulLinkSession.IsActive)
        {
            GD.Print("[SoulLink] Solo run — session inactive.");
            return;
        }

        GD.Print($"[SoulLink] Soul Link active. Shared HP: {SoulLinkSession.CurrentHp}/{SoulLinkSession.MaxHp}, Gold: {SoulLinkSession.Gold}");

        // Register to receive canonical gold broadcasts from the other peer.
        // SoulLinkGoldSyncMessage is auto-registered by STS2 because it implements INetMessage
        // and STS2 scans mod assemblies via ReflectionHelper.GetSubtypesInMods<INetMessage>().
        RunManager.Instance!.NetService.RegisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);

        // Broadcast initial canonical gold to fix any divergence from Neow bonuses or
        // save-load gold changes that fired before IsActive was set.
        RunManager.Instance.NetService.SendMessage(
            new SoulLinkGoldSyncMessage { CanonicalGold = SoulLinkSession.Gold });
    }
}

[HarmonyPatch(typeof(RunManager), "CleanUp")]
public static class RunCleanUpPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        GD.Print("[SoulLink] Run ended. Clearing session.");

        RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);

        SoulLinkSession.OnRunEnd();
    }
}
