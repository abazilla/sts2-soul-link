using System;
using Godot;

namespace SoulLinkMod;

/// <summary>
/// Central logger. Errors/Warn/Info always print so end-user bug reports
/// retain crash + boot context. Debug() is silent unless the
/// SOULLINK_DEBUG=1 environment variable is set (dev-only).
/// </summary>
public static class SoulLinkLog
{
    public static bool DebugEnabled { get; } =
        System.Environment.GetEnvironmentVariable("SOULLINK_DEBUG") == "1";

    public static void Debug(string msg)
    {
        if (DebugEnabled) GD.Print($"[SoulLink][DBG] {msg}");
    }

    public static void Info(string msg)  => GD.Print($"[SoulLink] {msg}");
    public static void Warn(string msg)  => GD.PrintErr($"[SoulLink][WARN] {msg}");
    public static void Error(string msg) => GD.PrintErr($"[SoulLink][ERR] {msg}");
}
