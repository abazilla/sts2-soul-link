using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Hooks NCombatRoom._Ready and each non-combat room's _Ready to inject Soul Link UI panels.
/// Each panel is wrapped in a CanvasLayer (Layer=100) so it renders above the game's own UI.
/// Hooks _ExitTree on each room type to clear static panel references.
///
/// Confirmed room class names (MegaCrit.Sts2.Core.Nodes.Rooms):
///   NCombatRoom, NMapRoom, NEventRoom, NRestSiteRoom, NTreasureRoom, NMerchantRoom
/// </summary>

[HarmonyPatch]
public static class CombatRoomReadyPatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom");
        return type != null ? AccessTools.Method(type, "_Ready") : null;
    }

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        if (!SoulLinkSession.IsActive) return;
        RoomPanelInjector.AddPanels(__instance);
    }
}

[HarmonyPatch]
public static class CombatRoomExitPatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom");
        return type != null ? AccessTools.Method(type, "_ExitTree") : null;
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        CombatLogPanel.Clear();
        RunStatsPanel.Clear();
        DebugOverlay.Clear();
    }
}

[HarmonyPatch]
public static class NonCombatRoomReadyPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        const string ns = "MegaCrit.Sts2.Core.Nodes.Rooms";
        foreach (var name in new[] { "NMapRoom", "NEventRoom", "NRestSiteRoom", "NTreasureRoom", "NMerchantRoom" })
        {
            var type = AccessTools.TypeByName($"{ns}.{name}");
            if (type == null)
            {
                GD.PrintErr($"[SoulLink] Room type {name} not found — panels won't appear there.");
                continue;
            }
            var m = AccessTools.Method(type, "_Ready");
            if (m != null) yield return m;
        }
    }

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        if (!SoulLinkSession.IsActive) return;
        RoomPanelInjector.AddPanels(__instance);
    }
}

[HarmonyPatch]
public static class NonCombatRoomExitPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        const string ns = "MegaCrit.Sts2.Core.Nodes.Rooms";
        foreach (var name in new[] { "NMapRoom", "NEventRoom", "NRestSiteRoom", "NTreasureRoom", "NMerchantRoom" })
        {
            var type = AccessTools.TypeByName($"{ns}.{name}");
            if (type == null) continue;
            var m = AccessTools.Method(type, "_ExitTree");
            if (m != null) yield return m;
        }
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        CombatLogPanel.Clear();
        RunStatsPanel.Clear();
        DebugOverlay.Clear();
    }
}

/// <summary>
/// Shared helper: creates the three Soul Link UI panels and adds each to its own
/// CanvasLayer (Layer=100) so they render above the game's built-in UI layers.
/// </summary>
internal static class RoomPanelInjector
{
    internal static void AddPanels(Node room)
    {
        // Re-send settings from the host on every room entry.
        // The run-start send in RunLaunchPatch races the client's handler registration;
        // by the time any room is ready, both machines are fully loaded and the handler
        // is guaranteed to be registered, so this delivery is reliable.
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState != null && LocalContext.IsMe(runState.Players[0]))
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            RunManager.Instance!.NetService.SendMessage(new SoulLinkSettingsSyncMessage
            {
                SplitMaxHp = rs.SplitMaxHp,
                SplitHeal  = rs.SplitHeal,
                ShareGold  = rs.ShareGold,
                SplitGold  = rs.SplitGold,
            });
            GD.Print("[SoulLink] Re-sent settings sync from host at room entry.");
        }

        // Panel visibility is a LOCAL setting — each player shows/hides their own panels
        // independently regardless of what the host chose.
        var ls = SoulLinkSettings.Instance;

        // CombatLogPanel — kill-feed style HP/gold log, top-right corner.
        if (ls.ShowCombatLog)
        {
            var combatLogLayer = new CanvasLayer { Layer = 100 };
            room.AddChild(combatLogLayer);
            var combatLog = new CombatLogPanel();
            combatLogLayer.AddChild(combatLog);
            combatLog.Initialize();
        }

        // RunStatsPanel — cumulative run totals, left side.
        if (ls.ShowRunStats)
        {
            var statsLayer = new CanvasLayer { Layer = 100 };
            room.AddChild(statsLayer);
            var stats = new RunStatsPanel();
            statsLayer.AddChild(stats);
            stats.Initialize();
        }

        // DebugOverlay — live sync diagnostics, left side.
        if (ls.ShowDebugOverlay)
        {
            var debugLayer = new CanvasLayer { Layer = 100 };
            room.AddChild(debugLayer);
            var debug = new DebugOverlay();
            debugLayer.AddChild(debug);
            debug.Initialize();
        }
    }
}
