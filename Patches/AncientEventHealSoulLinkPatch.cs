using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace SoulLinkMod.Patches;

/// <summary>
/// Dedups per-peer ancient-event heals (Pael, Darv, Tezcatara, etc.).
///
/// Vanilla AncientEventModel.BeforeEventStarted heals (MaxHp - CurrentHp), scaled by 0.8
/// at AscensionLevel.WearyTraveler+ (A2+). EventSynchronizer.BeginEvent iterates all
/// players, running this for each peer. In multiplayer with a shared HP pool the heal
/// would land N times. Edge case: BeginEvent can also be re-invoked (e.g. on save/load
/// retries), so a one-shot per-call flag isn't enough — we track by event id across the
/// whole room visit and let only the first peer's heal run.
///
/// Neow is excluded: its heal is part of the init-phase per-slot seeding that the rest
/// of the mod relies on. Skipping peers 1..N would leave their slots un-seeded.
///
/// _lastAppliedEventId is cleared when the event room exits (see SourceCapturePatch's
/// RoomExitSourceClearPatch — same hook also nulls CurrentRoomSource).
/// </summary>
[HarmonyPatch]
public static class AncientEventHealSoulLinkPatch
{
    internal static string? _lastAppliedEventId;

    static MethodBase TargetMethod() =>
        AccessTools.Method("MegaCrit.Sts2.Core.Models.AncientEventModel:BeforeEventStarted");

    [HarmonyPrefix]
    static bool Prefix(AncientEventModel __instance)
    {
        if (!SoulLinkSession.IsActive) return true;
        if (__instance is Neow) return true;

        string eventId = __instance.Id.Entry;
        if (_lastAppliedEventId == eventId)
        {
            SoulLinkLog.Debug($"[AncientHealPatch] Skipping duplicate peer heal for event {eventId}");
            return false;
        }

        SoulLinkLog.Debug($"[AncientHealPatch] First peer heal for event {eventId}; allowing");
        _lastAppliedEventId = eventId;
        SoulLinkSession.PendingSource = "@event:AncientHeal";
        return true;
    }
}
