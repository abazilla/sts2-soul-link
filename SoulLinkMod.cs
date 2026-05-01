using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace SoulLinkMod;

[ModInitializer(nameof(Initialize))]
public static class SoulLinkMod
{
    public const string Id = "soullink";
    public static string Version { get; private set; } = "unknown";

    /// <summary>
    /// Set to true while SoulLinkSession.ApplyToAllPlayers() is writing canonical
    /// values back to player objects, preventing the sync patches from re-firing.
    /// </summary>
    public static bool ApplyingCanonical { get; set; }

    public static void Initialize()
    {
        try
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            var json = File.ReadAllText(Path.Combine(dir, "SoulLink.json"));
            var m = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
            if (m.Success) Version = m.Groups[1].Value;
        }
        catch { /* Version stays "unknown" */ }

        try
        {
            var harmony = new Harmony(Id);

            Type[] types;
            try
            {
                types = Assembly.GetExecutingAssembly().GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
                GD.PrintErr($"[SoulLink] {ex.LoaderExceptions.Length} type(s) failed to load — continuing with {types.Length} loaded.");
            }

            foreach (var type in types)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[SoulLink] Failed to patch {type.Name}: {ex}");
                }
            }

            GD.Print("[SoulLink] Initialized.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] Initialize() crashed: {ex}");
        }
    }
}
