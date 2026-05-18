using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace SoulLinkMod;

/// <summary>
/// Persistent user preferences for the Soul Link mod.
/// Loaded once at startup, saved whenever a setting changes.
///
/// Run-scoped settings (SplitMaxHp, SplitHeal, GoldMode) are the HOST's preferences
/// before a run starts. Once a run begins they are copied into
/// SoulLinkSession.ActiveRunSettings and locked for the duration.
///
/// Panel visibility settings (ShowCombatLog, ShowRunStats, ShowDebugOverlay) are
/// LOCAL to this machine and never broadcast.
/// </summary>
public class SoulLinkSettings
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    private static SoulLinkSettings? _instance;
    public static SoulLinkSettings Instance
    {
        get
        {
            if (_instance == null) Load();
            return _instance!;
        }
    }

    // ── Run settings (host-owned, broadcast to clients at run start) ──────────

    public bool SplitMaxHp   { get; set; } = false;
    public bool SplitHeal    { get; set; } = false;
    public GoldSharingMode GoldMode { get; set; } = GoldSharingMode.Default;
    /// <summary>
    /// When true, "lose HP" relics and cards (CentennialPuzzle, SelfFormingClay,
    /// EmotionChip, Spite, Tear Asunder, etc.) also trigger for a player's teammates
    /// when a teammate takes unblocked damage.
    /// </summary>
    public bool SharedLoseHp { get; set; } = true;
    public HpMode HpMode { get; set; } = HpMode.SharedPool;
    public StartingHpMode StartingHpMode { get; set; } = StartingHpMode.Average;

    // ── Panel visibility (local-only, never broadcast) ────────────────────────

    public bool ShowCombatLog    { get; set; } = true;
    public bool ShowRunStats     { get; set; } = false;
    public bool ShowDebugOverlay { get; set; } = false;

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>Converts run-scoped fields into a SoulLinkRunSettings struct (panel fields are local-only).</summary>
    public SoulLinkRunSettings ToRunSettings() => new SoulLinkRunSettings
    {
        SplitMaxHp   = SplitMaxHp,
        SplitHeal    = SplitHeal,
        GoldMode     = GoldMode,
        SharedLoseHp = SharedLoseHp,
        HpMode       = HpMode,
        StartingHpMode = StartingHpMode,
    };

    // ── Persistence ───────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = true, // SoulLinkRunSettings uses public fields, not properties
    };

    private static string SettingsPath =>
        Path.Combine(OS.GetUserDataDir(), "soullink_settings.json");

    private static string RunSettingsPath =>
        Path.Combine(OS.GetUserDataDir(), "soullink_run.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                _instance = JsonSerializer.Deserialize<SoulLinkSettings>(json, _jsonOptions)
                            ?? new SoulLinkSettings();
                SoulLinkLog.Debug("Settings loaded.");
                return;
            }
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Failed to load settings: {ex.Message}");
        }

        _instance = new SoulLinkSettings();
        SoulLinkLog.Debug("Using default settings.");
    }

    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Instance, _jsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the active run settings to disk so save-load can restore them.
    /// Called at run start (both fresh and save-load).
    /// </summary>
    public static void SaveRunSettings(SoulLinkRunSettings r)
    {
        try
        {
            string json = JsonSerializer.Serialize(r, _jsonOptions);
            File.WriteAllText(RunSettingsPath, json);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Failed to save run settings: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads the run settings saved when this run first started.
    /// Returns null if no file exists (first time this run has launched).
    /// </summary>
    public static SoulLinkRunSettings? LoadRunSettings()
    {
        try
        {
            if (File.Exists(RunSettingsPath))
            {
                string json = File.ReadAllText(RunSettingsPath);
                return JsonSerializer.Deserialize<SoulLinkRunSettings>(json, _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Failed to load run settings: {ex.Message}");
        }
        return null;
    }
}
