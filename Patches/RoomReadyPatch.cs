using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Hooks NCombatRoom._Ready to inject CombatLogPanel.
/// Hooks _ExitTree on NCombatRoom to clear the panel reference.
/// Hooks each confirmed non-combat room's _Ready to inject RunStatsPanel.
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

        var panel = new CombatLogPanel();
        __instance.AddChild(panel);
        panel.Initialize();
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
    static void Postfix() => CombatLogPanel.Clear();
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
                GD.PrintErr($"[SoulLink] Room type {name} not found — RunStatsPanel won't appear there.");
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

        var panel = new RunStatsPanel();
        __instance.AddChild(panel);
        panel.Initialize();
    }
}
