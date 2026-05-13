using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace SoulLinkMod.Patches;

/// <summary>
/// Captures the vanilla lobby-phase NetService instance so Soul Link settings can
/// be sent/received before RunManager.Instance.NetService is populated.
///
/// Vanilla lobby networking lives on:
///   MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby (host)
///   MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby  (host, load-save flow)
///   MegaCrit.Sts2.Core.Multiplayer.Game.JoinFlow            (client)
///
/// All three hold a private `_netService` field. We Harmony-patch every declared
/// instance method on each type (postfix) and lazily snapshot that field. Capture
/// is idempotent and skips silently if the field/method shape is missing.
/// </summary>
internal static class LobbyNetCapture
{
    internal static object? Service;
    internal static Type? ServiceType;
    private static MethodInfo? _sendOpen;
    private static MethodInfo? _registerOpen;
    private static MethodInfo? _unregisterOpen;

    internal static bool IsAvailable => Service != null && _sendOpen != null;

    /// <summary>
    /// Fires once after a NetService is captured (or recaptured after the previous
    /// instance was replaced). Used by SettingsSyncHandler to re-run TryRegister now
    /// that a service is reachable.
    /// </summary>
    internal static event Action? OnCaptured;

    internal static void TryCapture(object? instance, string source)
    {
        if (instance == null) return;
        try
        {
            var type = instance.GetType();
            var fld = AccessTools.Field(type, "_netService");
            object? svc = fld?.GetValue(instance);
            if (svc == null)
            {
                var prop = AccessTools.Property(type, "NetService");
                svc = prop?.GetValue(instance);
            }
            if (svc == null) return;
            if (ReferenceEquals(Service, svc)) return;

            var svcType = svc.GetType();
            var send = FindGenericMethod(svcType, "SendMessage", 1);
            var reg  = FindGenericMethod(svcType, "RegisterMessageHandler", 1);
            var unreg = FindGenericMethod(svcType, "UnregisterMessageHandler", 1);
            if (send == null)
            {
                GD.Print($"[SoulLink][SyncDiag] LobbyNetCapture: found service {svcType.FullName} from {source} but no SendMessage<T>(T) method; skipping.");
                return;
            }

            Service = svc;
            ServiceType = svcType;
            _sendOpen = send;
            _registerOpen = reg;
            _unregisterOpen = unreg;
            GD.Print($"[SoulLink][SyncDiag] LobbyNetCapture: captured {svcType.FullName} (hash {svc.GetHashCode()}) from {source} (send={send != null}, reg={reg != null}, unreg={unreg != null})");
            try { OnCaptured?.Invoke(); }
            catch (Exception ex) { GD.PrintErr($"[SoulLink] LobbyNetCapture OnCaptured handler threw: {ex}"); }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink][SyncDiag] LobbyNetCapture.TryCapture from {source} failed: {ex.Message}");
        }
    }

    private static MethodInfo? FindGenericMethod(Type type, string name, int paramCount)
    {
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (m.Name != name) continue;
            if (!m.IsGenericMethodDefinition) continue;
            if (m.GetParameters().Length != paramCount) continue;
            return m;
        }
        return null;
    }

    internal static bool TrySend<T>(T msg) where T : struct
    {
        if (Service == null || _sendOpen == null) return false;
        try
        {
            var closed = _sendOpen.MakeGenericMethod(typeof(T));
            closed.Invoke(Service, new object?[] { msg });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] LobbyNetCapture.TrySend<{typeof(T).Name}> failed: {ex.Message}");
            return false;
        }
    }

    internal static bool TryRegister<T>(Action<T, ulong> handler) where T : struct
    {
        if (Service == null || _registerOpen == null) return false;
        try
        {
            var closed = _registerOpen.MakeGenericMethod(typeof(T));
            var del = ConvertHandler(closed, handler);
            closed.Invoke(Service, new object?[] { del });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] LobbyNetCapture.TryRegister<{typeof(T).Name}> failed: {ex.Message}");
            return false;
        }
    }

    internal static bool TryUnregister<T>(Action<T, ulong> handler) where T : struct
    {
        if (Service == null || _unregisterOpen == null) return false;
        try
        {
            var closed = _unregisterOpen.MakeGenericMethod(typeof(T));
            var del = ConvertHandler(closed, handler);
            closed.Invoke(Service, new object?[] { del });
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] LobbyNetCapture.TryUnregister<{typeof(T).Name}> failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Vanilla NetService expects MessageHandlerDelegate&lt;T&gt;, not Action&lt;T,ulong&gt;.
    /// Re-bind the handler's Method/Target onto the expected delegate type by reflecting
    /// the closed generic method's parameter.
    /// </summary>
    private static Delegate ConvertHandler(MethodInfo closedGeneric, Delegate handler)
    {
        var paramType = closedGeneric.GetParameters()[0].ParameterType;
        if (paramType.IsInstanceOfType(handler)) return handler;
        return Delegate.CreateDelegate(paramType, handler.Target, handler.Method);
    }
}

// ── Harmony patches: capture from every declared instance method on each lobby type.
// We don't know which method runs first (ctor may fire before _netService is assigned),
// so patch broadly and let TryCapture short-circuit on no-change. Compiler-generated
// state-machine methods are skipped.

[HarmonyPatch]
internal static class StartRunLobbyCapturePatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
        LobbyCaptureUtil.EnumerateTargets("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby");

    [HarmonyPostfix]
    static void Postfix(object __instance) =>
        LobbyNetCapture.TryCapture(__instance, "StartRunLobby");
}

[HarmonyPatch]
internal static class LoadRunLobbyCapturePatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
        LobbyCaptureUtil.EnumerateTargets("MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby");

    [HarmonyPostfix]
    static void Postfix(object __instance) =>
        LobbyNetCapture.TryCapture(__instance, "LoadRunLobby");
}

[HarmonyPatch]
internal static class JoinFlowCapturePatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
        LobbyCaptureUtil.EnumerateTargets("MegaCrit.Sts2.Core.Multiplayer.Game.JoinFlow");

    [HarmonyPostfix]
    static void Postfix(object __instance) =>
        LobbyNetCapture.TryCapture(__instance, "JoinFlow");
}

internal static class LobbyCaptureUtil
{
    internal static IEnumerable<MethodBase> EnumerateTargets(string typeName)
    {
        var type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            GD.Print($"[SoulLink][SyncDiag] LobbyNetCapture: type not found: {typeName}");
            return Enumerable.Empty<MethodBase>();
        }
        var ctors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Cast<MethodBase>();
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                !m.IsAbstract &&
                !m.ContainsGenericParameters &&
                m.DeclaringType == type &&
                // Skip compiler-generated state-machine MoveNext etc.
                !m.Name.StartsWith("<") &&
                // Skip property accessors — they are cheap noise.
                !m.IsSpecialName)
            .Cast<MethodBase>();
        return ctors.Concat(methods).ToList();
    }
}
