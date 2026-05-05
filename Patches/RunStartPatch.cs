using System;
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
    private static bool _registered;
    // Track which NetService instance we registered on. The lobby and run-time game
    // service are different objects — if the instance changes we must re-register on
    // the new one even if _registered is already true.
    private static object? _registeredNet;

    /// <summary>
    /// Registers the handler on the current NetService. Safe to call multiple times
    /// (lobby init, run start). Re-registers automatically when the NetService instance
    /// changes (e.g. lobby service → run-time game service transition).
    /// </summary>
    internal static void TryRegister()
    {
        var net = RunManager.Instance?.NetService;
        if (net == null) return;
        // Already registered on this exact instance — nothing to do.
        if (_registered && ReferenceEquals(_registeredNet, net)) return;
        net.RegisterMessageHandler<SoulLinkSettingsSyncMessage>(Handle);
        _registered = true;
        _registeredNet = net;
        GD.Print("[SoulLink] SettingsSyncHandler registered.");
    }

    /// <summary>Unregisters the handler if currently registered.</summary>
    internal static void TryUnregister()
    {
        if (!_registered) return;
        RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkSettingsSyncMessage>(Handle);
        _registered = false;
        _registeredNet = null;
    }

    /// <summary>
    /// Receives the host's run settings and locks them into the session.
    /// </summary>
    internal static void Handle(SoulLinkSettingsSyncMessage message, ulong senderId)
    {
        var settings = new SoulLinkRunSettings
        {
            SplitMaxHp   = message.SplitMaxHp,
            SplitHeal    = message.SplitHeal,
            GoldMode     = (GoldSharingMode)message.GoldMode,
            SharedLoseHp = message.SharedLoseHp,
        };

        if (SoulLinkSession.IsActive)
        {
            // Normal path: run is already active on this peer, apply immediately.
            SoulLinkSession.ActiveRunSettings = settings;

            // Re-sync _playerGold from actual player values when switching to SplitByPlayer.
            // Handles the timing issue where OnRunStart ran with a different GoldMode.
            if (settings.GoldMode == GoldSharingMode.SplitByPlayer)
            {
                var runState = RunManager.Instance?.DebugOnlyGetState();
                if (runState != null) SoulLinkSession.ReinitPlayerGold(runState);
            }
        }
        else
        {
            // Early path: host's message arrived before our RunManager.Launch() postfix ran.
            // Store it so OnRunStart() can pick it up instead of using local settings.
            SoulLinkSession.PendingSyncedRunSettings = settings;
        }

        GD.Print($"[SoulLink] Settings synced from host (IsActive={SoulLinkSession.IsActive}): SplitMaxHp={message.SplitMaxHp}, SplitHeal={message.SplitHeal}, GoldMode={(GoldSharingMode)message.GoldMode}");

        // Refresh the settings panel on the client (read-only view).
        UI.SoulLinkSettingsPanel.Current?.Refresh();
    }
}

internal static class GoldSyncHandler
{
    /// <summary>
    /// Receives a gold broadcast from the other peer and applies it locally.
    /// Registered as a message handler when the run starts, unregistered when it ends.
    ///
    /// SharedPool uses delta-based accumulation rather than absolute overwrite.
    /// Both players earn their own combat rewards independently (each machine only
    /// applies its own local player's event). If the handler used absolute values,
    /// the last broadcast received would overwrite the other — creating a race
    /// condition where the final canonical reflects only one player's reward.
    /// With deltas, each machine accumulates both contributions regardless of order:
    ///   HOST:   +19 (local) → receives +16 delta → G+35 ✓
    ///   CLIENT: +16 (local) → receives +19 delta → G+35 ✓
    ///
    /// Delta=0 is the initial run-start sync — treated as an absolute reset to
    /// fix any divergence from Neow bonuses that fired before IsActive was set.
    /// </summary>
    internal static void Handle(SoulLinkGoldSyncMessage message, ulong senderId)
    {
        if (!SoulLinkSession.IsActive) return;
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;

        // In SharedPool mode the remote player's setter may have already applied this
        // delta deterministically (e.g. monster gold-steal fires on both machines).
        // Consume the cancellation entry and skip to avoid a double-apply.
        if (goldMode == GoldSharingMode.SharedPool
            && message.Delta != 0
            && GoldSyncPatch.TryConsumeCancellation(message.PlayerSlot, message.Delta))
            return;

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            if (goldMode == GoldSharingMode.SplitByPlayer)
            {
                SoulLinkSession.SetGoldDirect(message.CanonicalGold, message.PlayerSlot);

                // Update the sender's player object with their canonical.
                if (message.PlayerSlot < runState.Players.Count)
                    runState.Players[message.PlayerSlot].Gold = message.CanonicalGold;

                // For gains, every OTHER player on this machine also receives their split share.
                // Delta already carries the scaled amount (e.g. 167 from a 333 gain with 2 players).
                if (message.Delta > 0)
                {
                    for (int i = 0; i < runState.Players.Count; i++)
                    {
                        if (i == message.PlayerSlot) continue;
                        int newGold = Math.Max(0, SoulLinkSession.GetPlayerGold(i) + message.Delta);
                        SoulLinkSession.SetGoldDirect(newGold, i);
                        runState.Players[i].Gold = newGold;
                    }
                }
            }
            else
            {
                // SharedPool: delta-based accumulation to avoid the race condition where
                // concurrent independent reward events on both machines overwrite each other.
                // Delta=0 is the initial run-start sync — use absolute canonical as a reset.
                int newCanonical = message.Delta == 0
                    ? message.CanonicalGold
                    : Math.Max(0, SoulLinkSession.Gold + message.Delta);
                SoulLinkSession.SetGoldDirect(newCanonical);
                foreach (var player in runState.Players)
                    player.Gold = newCanonical;
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }

        // Log the sender's change. Delta=0 is an initial sync — skip it.
        SoulLinkSession.LogGoldEntry(message.Delta, message.PlayerSlot,
            SoulLinkSession.CurrentRoomSource);

        // Log each other player's gain in SplitByPlayer mode.
        if (goldMode == GoldSharingMode.SplitByPlayer && message.Delta > 0)
        {
            for (int i = 0; i < runState.Players.Count; i++)
            {
                if (i == message.PlayerSlot) continue;
                SoulLinkSession.LogGoldEntry(message.Delta, i, SoulLinkSession.CurrentRoomSource);
            }
        }

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
            SettingsSyncHandler.TryRegister(); // safe even if lobby already registered it
        }

        SoulLinkSession.OnRunStart();

        if (!SoulLinkSession.IsActive)
        {
            // Solo run — unregister what we just registered.
            RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
            SettingsSyncHandler.TryUnregister();
            GD.Print("[SoulLink] Solo run — session inactive.");
            return;
        }

        GD.Print($"[SoulLink] Soul Link active. Shared HP: {SoulLinkSession.CurrentHp}/{SoulLinkSession.MaxHp}, GoldMode: {SoulLinkSession.ActiveRunSettings.GoldMode}");

        // Only the host broadcasts settings. The host is the machine whose local player is slot 0.
        bool isHost = LocalContext.IsMe(runState.Players[0]);
        GD.Print($"[SoulLink] IsHost={isHost} — SplitMaxHp={SoulLinkSession.ActiveRunSettings.SplitMaxHp}, GoldMode={SoulLinkSession.ActiveRunSettings.GoldMode}");

        if (isHost)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            RunManager.Instance!.NetService.SendMessage(new SoulLinkSettingsSyncMessage
            {
                SplitMaxHp   = rs.SplitMaxHp,
                SplitHeal    = rs.SplitHeal,
                GoldMode     = (int)rs.GoldMode,
                SharedLoseHp = rs.SharedLoseHp,
            });
        }

        // Broadcast initial canonical gold to fix any divergence from Neow bonuses or
        // save-load gold changes that fired before IsActive was set.
        // Only needed for SharedPool — in SplitByPlayer each machine knows its own player's gold;
        // in Default STS2 manages gold natively.
        if (SoulLinkSession.ActiveRunSettings.GoldMode == GoldSharingMode.SharedPool)
        {
            RunManager.Instance!.NetService.SendMessage(
                new SoulLinkGoldSyncMessage { CanonicalGold = SoulLinkSession.Gold });
        }
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
        SettingsSyncHandler.TryUnregister();
        GoldSyncPatch.ClearCancellations();

        SoulLinkSession.OnRunEnd();
    }
}
