using System.Reflection;
using Godot;
using HarmonyLib;

namespace SoulLinkMod.Patches;

/// <summary>
/// Sets SoulLinkSession.CurrentRoomSource when entering non-combat rooms so that
/// HP/gold log entries are labelled with the room context (event name, "Rest Site", etc.)
/// rather than the generic "Out of combat" fallback.
///
/// Uses the same room-type hooks as RoomReadyPatch.
/// </summary>

// ── Event rooms — capture the event's display name via NEventRoom.Title ──────

[HarmonyPatch]
static class EventRoomSourcePatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom");
        return type != null ? AccessTools.Method(type, "_Ready") : null;
    }

    [HarmonyPostfix]
    static void Postfix(Node __instance)
    {
        if (!SoulLinkSession.IsActive) return;
        try
        {
            // _event is a private EventModel field set in NEventRoom.Create() before _Ready().
            // EventModel.Id.Entry gives the raw ID string e.g. "NEOW", "MYSTERIOUS_ORB".
            // We convert to title case: "MYSTERIOUS_ORB" → "Mysterious Orb".
            var eventField = AccessTools.Field(__instance.GetType(), "_event");
            var eventModel = eventField?.GetValue(__instance);
            string? name = null;
            if (eventModel != null)
            {
                // Id may be on the type or a base type
                var idProp = AccessTools.Property(eventModel.GetType(), "Id")
                    ?? AccessTools.Property(eventModel.GetType().BaseType, "Id");
                var idObj = idProp?.GetValue(eventModel);
                if (idObj != null)
                {
                    var entryProp = AccessTools.Property(idObj.GetType(), "Entry");
                    string? entry = entryProp?.GetValue(idObj)?.ToString();
                    if (!string.IsNullOrEmpty(entry))
                    {
                        name = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                            .ToTitleCase(entry.Replace("_", " ").ToLower());
                    }
                }
            }
            SoulLinkSession.CurrentRoomSource = !string.IsNullOrWhiteSpace(name) ? name : "Event";
        }
        catch
        {
            SoulLinkSession.CurrentRoomSource = "Event";
        }
    }
}

// ── Rest site ─────────────────────────────────────────────────────────────────

[HarmonyPatch]
static class RestSiteSourcePatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom");
        return type != null ? AccessTools.Method(type, "_Ready") : null;
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkSession.CurrentRoomSource = "Rest Site";
    }
}

// ── Merchant ──────────────────────────────────────────────────────────────────

[HarmonyPatch]
static class MerchantSourcePatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom");
        return type != null ? AccessTools.Method(type, "_Ready") : null;
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkSession.CurrentRoomSource = "Shop";
    }
}

// ── Treasure room ─────────────────────────────────────────────────────────────

[HarmonyPatch]
static class TreasureSourcePatch
{
    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom");
        return type != null ? AccessTools.Method(type, "_Ready") : null;
    }

    [HarmonyPostfix]
    static void Postfix()
    {
        if (!SoulLinkSession.IsActive) return;
        SoulLinkSession.CurrentRoomSource = "Treasure";
    }
}

// ── Clear on any non-combat room exit ─────────────────────────────────────────
// (RoomReadyPatch.NonCombatRoomExitPatch already fires for all 5 room types;
//  we piggyback on the same methods to clear CurrentRoomSource.)

[HarmonyPatch]
static class RoomExitSourceClearPatch
{
    static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
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
    static void Postfix() => SoulLinkSession.CurrentRoomSource = null;
}
