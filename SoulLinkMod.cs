using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SoulLinkMod.Localization;

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

    /// <summary>
    /// Set to true during BurningBlood.AfterCombatVictory to suppress VGQ broadcast
    /// of the end-of-combat heal. Each peer applies the heal locally (deterministic),
    /// so broadcasting would only cause an OwnerId=0 enqueue error and softlock.
    /// </summary>
    public static bool SuppressVgqEnqueue { get; set; }

    /// <summary>
    /// True while an NCombatRoom is mounted (between _Ready and _ExitTree). Vanilla's
    /// CombatManager.IsInProgress flips later — during _Ready the room mode is already
    /// ActiveCombat but IsInProgress is still false. HP/MaxHp/Gold writes that hit during
    /// that window would otherwise enqueue NonCombat VGQ actions that jam the queue
    /// once combat officially begins. Maintained by CombatRoomReadyPatch / CombatRoomExitPatch.
    /// </summary>
    public static bool CombatRoomActive { get; set; }

    /// <summary>
    /// True if either vanilla considers a combat in progress, or an NCombatRoom is
    /// mounted (covers the _Ready window before IsInProgress flips). Use this in
    /// place of CombatManager.Instance.IsInProgress when classifying VGQ actions or
    /// gating broadcast paths around the combat-onset boundary.
    /// </summary>
    public static bool IsCombatActive()
    {
        bool vanilla = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress == true;
        // Once vanilla has flipped IsInProgress=true, our bridge flag has served its
        // purpose (covering the _Ready window before IsInProgress flipped). Clear it
        // so it doesn't shadow the post-combat window between IsInProgress→false and
        // NCombatRoom._ExitTree, when Burning Blood EOC heals fire and must scale.
        if (vanilla) CombatRoomActive = false;
        return vanilla || CombatRoomActive;
    }

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

        // Initialize feature flag system
        FeatureFlagManager.Initialize();
        FeatureFlagManager.SetFlag(FeatureFlag.NetworkedActions, true); // default to enabled for testing

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
                SoulLinkLog.Error($"{ex.LoaderExceptions.Length} type(s) failed to load — continuing with {types.Length} loaded.");
            }

            foreach (var type in types)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    SoulLinkLog.Error($"Failed to patch {type.Name}: {ex}");
                }
            }

            SoulLinkLog.Info($"Initialized v{Version}.");

            // Localization tables load lazily on first Loc.T/Loc.S call — see
            // Localization/LocTableLoader.cs. Eager merge here would NRE because
            // LocManager.Instance is null this early in the boot sequence
            // (bd: sts2-soul-link-q60). EnsureLoaded retries until LocManager exists.
            LocTableLoader.EnsureLoaded();
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Initialize() crashed: {ex}");
        }
    }
}
