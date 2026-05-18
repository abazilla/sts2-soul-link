using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.Actions;
using SoulLinkMod.Messages;
using SoulLinkMod.UI;

using SoulLinkMod;

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
    // Run-time NetService (RunManager.Instance.NetService) — null until run starts.
    private static bool _registeredRun;
    private static object? _registeredRunNet;
    // Lobby-phase NetService (captured by LobbyNetCapture from StartRunLobby /
    // LoadRunLobby / JoinFlow). Different object from the run-time service.
    private static bool _registeredLobby;
    private static object? _registeredLobbyNet;

    static SettingsSyncHandler()
    {
        // Re-attempt registration whenever a lobby service is captured for the first
        // time (or replaced). CharSelectClientInitPatch may have called TryRegister
        // before any service existed; this fires the moment one appears.
        LobbyNetCapture.OnCaptured += TryRegister;
    }

    /// <summary>
    /// Registers the handler on whichever NetService instances are currently reachable
    /// (run-time and/or lobby). Safe to call repeatedly — only registers when the
    /// underlying service instance changes.
    /// </summary>
    internal static void TryRegister()
    {
        var run = RunManager.Instance?.NetService;
        if (run != null && !(_registeredRun && ReferenceEquals(_registeredRunNet, run)))
        {
            run.RegisterMessageHandler<SoulLinkSettingsSyncMessage>(Handle);
            _registeredRun = true;
            _registeredRunNet = run;
            SoulLinkLog.Debug($"[SyncDiag] TryRegister: REGISTERED on run net#{run.GetHashCode()} IsConnected={run.IsConnected}");
        }

        if (LobbyNetCapture.IsAvailable && !(_registeredLobby && ReferenceEquals(_registeredLobbyNet, LobbyNetCapture.Service)))
        {
            if (LobbyNetCapture.TryRegister<SoulLinkSettingsSyncMessage>(Handle))
            {
                _registeredLobby = true;
                _registeredLobbyNet = LobbyNetCapture.Service;
                SoulLinkLog.Debug($"[SyncDiag] TryRegister: REGISTERED on lobby net#{LobbyNetCapture.Service!.GetHashCode()}");
            }
        }

        if (run == null && !LobbyNetCapture.IsAvailable)
            SoulLinkLog.Debug("[SyncDiag] TryRegister: no net available (run=null, lobby=null).");
    }

    /// <summary>Unregisters the handler from any service it was registered on.</summary>
    internal static void TryUnregister()
    {
        if (_registeredRun)
        {
            RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkSettingsSyncMessage>(Handle);
            _registeredRun = false;
            _registeredRunNet = null;
        }
        if (_registeredLobby)
        {
            LobbyNetCapture.TryUnregister<SoulLinkSettingsSyncMessage>(Handle);
            _registeredLobby = false;
            _registeredLobbyNet = null;
        }
    }

    /// <summary>
    /// Receives the host's run settings and locks them into the session.
    /// </summary>
    internal static void Handle(SoulLinkSettingsSyncMessage message, ulong senderId)
    {
        SoulLinkLog.Debug($"[SyncDiag] Handle ENTRY senderId={senderId} localId={LocalContext.NetId} IsActive={SoulLinkSession.IsActive} payload(HpMode={(HpMode)message.HpMode} GoldMode={(GoldSharingMode)message.GoldMode} SplitMaxHp={message.SplitMaxHp})");
        // ShouldBroadcast=true means the host receives its own message. Host already owns
        // the authoritative settings — ignore the echo so we don't clobber state with stale
        // values on a re-entrant Refresh.
        if (LocalContext.NetId.HasValue && senderId == LocalContext.NetId.Value)
        {
            SoulLinkLog.Debug("[SyncDiag] Handle: self-echo, skipping.");
            return;
        }

        var settings = new SoulLinkRunSettings
        {
            SplitMaxHp   = message.SplitMaxHp,
            SplitHeal    = message.SplitHeal,
            GoldMode     = (GoldSharingMode)message.GoldMode,
            SharedLoseHp = message.SharedLoseHp,
            HpMode       = (HpMode)message.HpMode,
            StartingHpMode = (StartingHpMode)message.StartingHpMode,
        };

        if (SoulLinkSession.IsActive)
        {
            // Normal path: run is already active on this peer, apply immediately.
            var prevGoldMode = SoulLinkSession.ActiveRunSettings.GoldMode;
            SoulLinkSession.ActiveRunSettings = settings;

            // Vanilla HpMode never routes through ApplyHpDelta, so the in-combat trigger
            // that flips _initPhaseComplete never fires. Without this, AddEntry suppresses
            // all gold log entries on clients whose local default differed from the host's
            // Vanilla setting at OnRunStart time.
            if (settings.HpMode == HpMode.Vanilla)
                SoulLinkSession.MarkInitPhaseComplete();

            var runState = RunManager.Instance?.DebugOnlyGetState();

            // Re-sync gold from actual player values when the GoldMode changes.
            // This handles the timing issue where OnRunStart ran with a different GoldMode
            // (e.g. the initial SoulLinkSettingsSyncMessage was dropped during the load
            // screen and arrived late, after IsActive was set).
            if (settings.GoldMode == GoldSharingMode.SplitByPlayer)
            {
                if (runState != null) SoulLinkSession.ReinitPlayerGold(runState);
            }
            else if (settings.GoldMode == GoldSharingMode.SharedPool
                     && prevGoldMode != GoldSharingMode.SharedPool)
            {
                // Switched into SharedPool: Gold was 0 (Default/SplitByPlayer don't use it).
                // Re-seed from the local player's actual gold so the pool starts correctly.
                if (runState != null) SoulLinkSession.ReinitSharedGold(runState);
            }
        }
        else
        {
            // Early path: host's message arrived before our RunManager.Launch() postfix ran.
            // Store it so OnRunStart() can pick it up instead of using local settings.
            SoulLinkSession.PendingSyncedRunSettings = settings;
        }

        SoulLinkLog.Debug($"Settings synced from host (IsActive={SoulLinkSession.IsActive}): SplitMaxHp={message.SplitMaxHp}, SplitHeal={message.SplitHeal}, GoldMode={(GoldSharingMode)message.GoldMode}, HpMode={(HpMode)message.HpMode}");

        SoulLinkLog.Debug($"[SyncDiag] Handle: applied. PanelCurrent={(UI.SoulLinkSettingsPanel.Current != null)} -> Refresh()");
        // Refresh the settings panel on the client (read-only view).
        UI.SoulLinkSettingsPanel.Current?.Refresh();
    }
}

internal static class GoldChangeActionHandler
{
    private static bool _registered;
    private static object? _registeredNet;

    internal static void TryRegister()
    {
        var net = RunManager.Instance?.NetService;
        if (net == null) return;
        if (_registered && ReferenceEquals(_registeredNet, net)) return;
        net.RegisterMessageHandler<GoldChangeSyncMessage>(Handle);
        _registered = true;
        _registeredNet = net;
        SoulLinkLog.Debug("GoldChangeActionHandler registered.");
    }

    internal static void TryUnregister()
    {
        if (!_registered) return;
        RunManager.Instance?.NetService?.UnregisterMessageHandler<GoldChangeSyncMessage>(Handle);
        _registered = false;
        _registeredNet = null;
    }

    internal static void Handle(GoldChangeSyncMessage message, ulong senderId)
    {
        if (!SoulLinkSession.IsActive) return;
        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;
        if (goldMode == GoldSharingMode.Default) return;

        // In SharedPool mode, deterministic events (e.g. enemy gold steal) execute the same setter
        // on both machines. Both sides apply the delta independently and broadcast an action.
        // Check if this delta was already applied by a deterministic local setter (before the remote
        // action arrived). If so, skip to avoid double-apply.
        if (goldMode == GoldSharingMode.SharedPool &&
            message.DeltaGold != 0 &&
            GoldSyncPatch.TryConsumeCancellation(message.PlayerSlot, message.DeltaGold))
        {
            SoulLinkLog.Debug($"[GoldChangeAction] Consumed cancellation for slot={message.PlayerSlot} delta={message.DeltaGold}");
            return;
        }

        // Mark this action as applied so the setter (if it fires after this handler) can recognize it.
        if (goldMode == GoldSharingMode.SharedPool && message.DeltaGold != 0)
        {
            GoldSyncPatch.EnqueueNetworkApplied(message.PlayerSlot);
        }

        var action = new GoldChangeAction
        {
            DeltaGold = message.DeltaGold,
            PlayerSlot = message.PlayerSlot,
            Source = message.Source,
            WasBlocked = message.WasBlocked,
        };
        NetActionService.ExecuteRemoteAction(action, message.Timestamp, message.PlayerSlot);
    }
}

internal static class HpChangeActionHandler
{
    private static bool _registered;
    private static object? _registeredNet;

    internal static void TryRegister()
    {
        var net = RunManager.Instance?.NetService;
        if (net == null) return;
        if (_registered && ReferenceEquals(_registeredNet, net)) return;
        net.RegisterMessageHandler<HpChangeSyncMessage>(Handle);
        _registered = true;
        _registeredNet = net;
        SoulLinkLog.Debug("HpChangeActionHandler registered.");
    }

    internal static void TryUnregister()
    {
        if (!_registered) return;
        RunManager.Instance?.NetService?.UnregisterMessageHandler<HpChangeSyncMessage>(Handle);
        _registered = false;
        _registeredNet = null;
    }

    internal static void Handle(HpChangeSyncMessage message, ulong senderId)
    {
        if (!SoulLinkSession.IsActive) return;
        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return;

        // Skip during init phase (Neow) - it's deterministic, already processed locally.
        if (!SoulLinkSession.IsInitPhaseComplete)
        {
            SoulLinkLog.Debug($"[HpHandler] Skipping during init phase: slot={message.PlayerSlot} delta={message.DeltaHp}");
            return;
        }

        SoulLinkLog.Debug($"[HpHandler] Received: slot={message.PlayerSlot} delta={message.DeltaHp}");

        // Dedup is handled atomically inside HpChangeAction.Execute() via SyncCoordinator.
        var action = new HpChangeAction
        {
            DeltaHp = message.DeltaHp,
            PlayerSlot = message.PlayerSlot,
            InCombat = message.InCombat,
            Source = message.Source,
        };
        NetActionService.ExecuteRemoteAction(action, message.Timestamp, message.PlayerSlot);
    }
}

internal static class MaxHpChangeActionHandler
{
    private static bool _registered;
    private static object? _registeredNet;

    internal static void TryRegister()
    {
        var net = RunManager.Instance?.NetService;
        if (net == null) return;
        if (_registered && ReferenceEquals(_registeredNet, net)) return;
        net.RegisterMessageHandler<MaxHpChangeSyncMessage>(Handle);
        _registered = true;
        _registeredNet = net;
        SoulLinkLog.Debug("MaxHpChangeActionHandler registered.");
    }

    internal static void TryUnregister()
    {
        if (!_registered) return;
        RunManager.Instance?.NetService?.UnregisterMessageHandler<MaxHpChangeSyncMessage>(Handle);
        _registered = false;
        _registeredNet = null;
    }

    internal static void Handle(MaxHpChangeSyncMessage message, ulong senderId)
    {
        if (!SoulLinkSession.IsActive) return;
        if (SoulLinkSession.ActiveRunSettings.HpMode == HpMode.Vanilla) return;

        // Skip during init phase (Neow) - it's deterministic, already processed locally.
        if (!SoulLinkSession.IsInitPhaseComplete)
        {
            SoulLinkLog.Debug($"[MaxHpHandler] Skipping during init phase: slot={message.PlayerSlot} delta={message.DeltaMaxHp}");
            return;
        }

        SoulLinkLog.Debug($"[MaxHpHandler] Received: slot={message.PlayerSlot} delta={message.DeltaMaxHp}");

        // Dedup is handled atomically inside MaxHpChangeAction.Execute() via SyncCoordinator.
        var action = new MaxHpChangeAction
        {
            DeltaMaxHp = message.DeltaMaxHp,
            PlayerSlot = message.PlayerSlot,
            InCombat = message.InCombat,
            Source = message.Source,
        };
        NetActionService.ExecuteRemoteAction(action, message.Timestamp, message.PlayerSlot);
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
        var goldMode = SoulLinkSession.ActiveRunSettings.GoldMode;
        if (goldMode == GoldSharingMode.Default) return;
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        // In SharedPool mode the remote player's setter may have already applied this
        // delta deterministically (e.g. monster gold-steal fires on both machines).
        // Consume the cancellation entry and skip to avoid a double-apply.
        // (Handles: remote setter fired BEFORE this handler.)
        if (goldMode == GoldSharingMode.SharedPool
            && message.Delta != 0
            && GoldSyncPatch.TryConsumeCancellation(message.PlayerSlot, message.Delta))
            return;

        // Register this delta as network-applied so the remote setter can skip it if
        // OptionIndexChosenMessage (or any event replay) fires the setter AFTER we apply.
        // (Handles: this handler fires BEFORE the remote setter.)
        if (goldMode == GoldSharingMode.SharedPool && message.Delta != 0)
        {
            GoldSyncPatch.EnqueueNetworkApplied(message.PlayerSlot);
            SoulLinkLog.Debug($"[GoldSync] EnqueueNetworkApplied slot={message.PlayerSlot} delta={message.Delta}");
        }

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

        SoulLinkLog.Debug($"Run launched with {runState.Players.Count} player(s).");

        // Register handlers BEFORE OnRunStart so that if the host's SoulLinkSettingsSyncMessage
        // arrives before our own Launch() postfix completes, it lands in PendingSyncedRunSettings
        // and OnRunStart() picks it up.
        if (runState.Players.Count > 1)
        {
            RunManager.Instance!.NetService.RegisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
            GoldChangeActionHandler.TryRegister();
            HpChangeActionHandler.TryRegister();
            MaxHpChangeActionHandler.TryRegister();
            SettingsSyncHandler.TryRegister(); // safe even if lobby already registered it
        }

        SoulLinkSession.OnRunStart();

        if (!SoulLinkSession.IsActive)
        {
            // Solo run — unregister what we just registered.
            RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
            GoldChangeActionHandler.TryUnregister();
            HpChangeActionHandler.TryUnregister();
            MaxHpChangeActionHandler.TryUnregister();
            SettingsSyncHandler.TryUnregister();
            SoulLinkLog.Debug("Solo run — session inactive.");
            return;
        }

        SoulLinkLog.Debug($"Soul Link active. Shared HP: {SoulLinkSession.CurrentHp}/{SoulLinkSession.MaxHp}, GoldMode: {SoulLinkSession.ActiveRunSettings.GoldMode}");

        // Only the host broadcasts settings. The host is the machine whose local player is slot 0.
        bool isHost = LocalContext.IsMe(runState.Players[0]);
        SoulLinkLog.Debug($"IsHost={isHost} — SplitMaxHp={SoulLinkSession.ActiveRunSettings.SplitMaxHp}, GoldMode={SoulLinkSession.ActiveRunSettings.GoldMode}");

        if (isHost)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            RunManager.Instance!.NetService.SendMessage(new SoulLinkSettingsSyncMessage
            {
                SplitMaxHp   = rs.SplitMaxHp,
                SplitHeal    = rs.SplitHeal,
                GoldMode     = (int)rs.GoldMode,
                SharedLoseHp = rs.SharedLoseHp,
                HpMode       = (int)rs.HpMode,
                StartingHpMode = (int)rs.StartingHpMode,
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
        SoulLinkLog.Debug("Run ended. Clearing session.");

        RunManager.Instance?.NetService?.UnregisterMessageHandler<SoulLinkGoldSyncMessage>(GoldSyncHandler.Handle);
        GoldChangeActionHandler.TryUnregister();
        HpChangeActionHandler.TryUnregister();
        MaxHpChangeActionHandler.TryUnregister();
        SettingsSyncHandler.TryUnregister();
        GoldSyncPatch.ClearCancellations();
        NetActionService.Reset();

        SoulLinkSession.OnRunEnd();
    }
}
