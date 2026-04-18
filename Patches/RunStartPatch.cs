using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.Patches;

/// <summary>
/// Hooks RunManager.Launch() to initialize the Soul Link session when a run starts.
/// Hooks RunManager.CleanUp() to tear it down when the run ends.
///
/// RunState.Players is fully populated before Launch() fires — safe to read there.
/// Soul Link only activates for multiplayer runs (Players.Count > 1).
/// </summary>

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

        if (SoulLinkSession.IsActive)
            GD.Print($"[SoulLink] Soul Link active. Shared HP: {SoulLinkSession.CurrentHp}/{SoulLinkSession.MaxHp}, Gold: {SoulLinkSession.Gold}");
        else
            GD.Print("[SoulLink] Solo run — session inactive.");
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
        SoulLinkSession.OnRunEnd();
    }
}
