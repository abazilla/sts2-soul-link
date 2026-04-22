using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
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
internal static class SettingsSyncHandler
{
    /// <summary>
    /// Receives the host's run settings at run start and locks them into the session.
    /// Fires on both peers (host receives its own broadcast too, which is harmless).
    /// </summary>
    internal static void Handle(SoulLinkSettingsSyncMessage message, ulong senderId)
    {
        var settings = new SoulLinkRunSettings
        {
            SplitMaxHp = message.SplitMaxHp,
            SplitHeal  = message.SplitHeal,
            ShareGold  = message.ShareGold,
            SplitGold  = message.SplitGold,
        };

        if (SoulLinkSession.IsActive)
        {
            // Normal path: run is already active on this peer, apply immediately.
            SoulLinkSession.ActiveRunSettings = settings;
        }
        else
        {
            // Early path: host's message arrived before our RunManager.Launch() postfix ran.
            // Store it so OnRunStart() can pick it up instead of using local settings.
            SoulLinkSession.PendingSyncedRunSettings = settings;
        }

        GD.Print($"[SoulLink] Settings synced from host (IsActive={SoulLinkSession.IsActive}): SplitMaxHp={message.SplitMaxHp}, SplitHeal={message.SplitHeal}, ShareGold={message.ShareGold}, SplitGold={message.SplitGold}");

        // Refresh the settings panel on the client (read-only view).
        UI.SoulLinkSettingsPanel.Current?.Refresh();
    }
}

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

        // Register handlers BEFORE OnRunStart so that if the host's SoulLinkSettingsSyncMessage
        // arrives before our own Launch() postfix completes, it lands in PendingSyncedRunSettings
        // and OnRunStart() picks it up.
        if (runState.Players.Count > 1)
        {
            RunManager.Instance!.NetService.RegisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
            RunManager.Instance.NetService.RegisterMessageHandler<SoulLinkSettingsSyncMessage>(SettingsSyncHandler.Handle);
        }

        SoulLinkSession.OnRunStart();

        if (!SoulLinkSession.IsActive)
        {
            // Solo run — unregister what we just registered.
            RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
            RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkSettingsSyncMessage>(SettingsSyncHandler.Handle);
            GD.Print("[SoulLink] Solo run — session inactive.");
            return;
        }

        GD.Print($"[SoulLink] Soul Link active. Shared HP: {SoulLinkSession.CurrentHp}/{SoulLinkSession.MaxHp}, Gold: {SoulLinkSession.Gold}");

        // Only the host broadcasts settings. The host is the machine whose local player is slot 0.
        // Both machines run this code, but only one should send the authoritative settings.
        bool isHost = LocalContext.IsMe(runState.Players[0]);
        GD.Print($"[SoulLink] IsHost={isHost} — SplitMaxHp={SoulLinkSession.ActiveRunSettings.SplitMaxHp}, ShareGold={SoulLinkSession.ActiveRunSettings.ShareGold}");

        if (isHost)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            RunManager.Instance!.NetService.SendMessage(new SoulLinkSettingsSyncMessage
            {
                SplitMaxHp = rs.SplitMaxHp,
                SplitHeal  = rs.SplitHeal,
                ShareGold  = rs.ShareGold,
                SplitGold  = rs.SplitGold,
            });
        }

        // Broadcast initial canonical gold to fix any divergence from Neow bonuses or
        // save-load gold changes that fired before IsActive was set.
        // Each machine sends its own canonical so both sides are synced.
        RunManager.Instance!.NetService.SendMessage(
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
        RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkSettingsSyncMessage>(SettingsSyncHandler.Handle);

        SoulLinkSession.OnRunEnd();
    }
}
