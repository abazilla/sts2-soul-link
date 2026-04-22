using System.Reflection;
using Godot;
using HarmonyLib;
using SoulLinkMod.UI;

namespace SoulLinkMod.Patches;

/// <summary>
/// Injects the SoulLinkSettingsPanel into the multiplayer lobby screens so the host
/// can configure run settings before embarking.
///
/// Targets:
///   NCharacterSelectScreen — standard multiplayer (Standard / Daily modes as host)
///   NCustomRunScreen       — custom run multiplayer
///   NMultiplayerHostSubmenu — mode selection screen (always host-side)
///
/// Pattern:
///   _Ready()                    → create panel (hidden) and add to a CanvasLayer
///   InitializeMultiplayerAsHost → show panel in host (editable) mode
///   InitializeMultiplayerAsClient → show panel in client (read-only) mode
///   OnSubmenuClosed             → clear the static reference
/// </summary>

// ── NCharacterSelectScreen ─────────────────────────────────────────────────────

[HarmonyPatch]
public static class CharSelectReadyPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen"), "_Ready");

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        LobbyPanelInjector.InjectPanel(__instance);
    }
}

[HarmonyPatch]
public static class CharSelectHostInitPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen"),
            "InitializeMultiplayerAsHost");

    [HarmonyPostfix]
    static void Postfix()
    {
        SoulLinkSettingsPanel.Current?.Show(isHost: true);
    }
}

[HarmonyPatch]
public static class CharSelectClientInitPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen"),
            "InitializeMultiplayerAsClient");

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        if (SoulLinkSettingsPanel.Current == null)
            LobbyPanelInjector.InjectPanel(__instance);
        SoulLinkSettingsPanel.Current?.Show(isHost: false);
    }
}

[HarmonyPatch]
public static class CharSelectClosedPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen"),
            "OnSubmenuClosed");

    [HarmonyPostfix]
    static void Postfix()
    {
        SoulLinkSettingsPanel.Clear();
    }
}

// ── NCustomRunScreen ───────────────────────────────────────────────────────────

[HarmonyPatch]
public static class CustomRunReadyPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen"), "_Ready");

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        LobbyPanelInjector.InjectPanel(__instance);
    }
}

[HarmonyPatch]
public static class CustomRunHostInitPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen"),
            "InitializeMultiplayerAsHost");

    [HarmonyPostfix]
    static void Postfix()
    {
        SoulLinkSettingsPanel.Current?.Show(isHost: true);
    }
}

[HarmonyPatch]
public static class CustomRunClientInitPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen"),
            "InitializeMultiplayerAsClient");

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        if (SoulLinkSettingsPanel.Current == null)
            LobbyPanelInjector.InjectPanel(__instance);
        SoulLinkSettingsPanel.Current?.Show(isHost: false);
    }
}

[HarmonyPatch]
public static class CustomRunClosedPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen"),
            "OnSubmenuClosed");

    [HarmonyPostfix]
    static void Postfix()
    {
        SoulLinkSettingsPanel.Clear();
    }
}

// ── NMultiplayerHostSubmenu ────────────────────────────────────────────────────

[HarmonyPatch]
public static class MultiplayerHostSubmenuReadyPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerHostSubmenu"), "_Ready");

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        // This screen is always the host's, so show immediately in host mode.
        LobbyPanelInjector.InjectPanel(__instance);
        SoulLinkSettingsPanel.Current?.Show(isHost: true);
    }
}

[HarmonyPatch]
public static class MultiplayerHostSubmenuClosedPatch
{
    static MethodBase? TargetMethod() =>
        AccessTools.Method(
            AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerHostSubmenu"),
            "OnSubmenuClosed");

    [HarmonyPostfix]
    static void Postfix()
    {
        SoulLinkSettingsPanel.Clear();
    }
}

// ── Shared injection helper ────────────────────────────────────────────────────

internal static class LobbyPanelInjector
{
    /// <summary>
    /// Creates a SoulLinkSettingsPanel, wraps it in a CanvasLayer (Layer=50 — below
    /// the in-run overlays at 100 but above the game's base UI), and attaches it to
    /// the given lobby screen node.
    /// </summary>
    internal static void InjectPanel(Node screen)
    {
        try
        {
            var layer = new CanvasLayer { Layer = 50 };
            screen.AddChild(layer);
            var panel = new SoulLinkSettingsPanel();
            layer.AddChild(panel);
            panel.Initialize();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[SoulLink] LobbyPanelInjector.InjectPanel crashed: {ex}");
        }
    }
}
