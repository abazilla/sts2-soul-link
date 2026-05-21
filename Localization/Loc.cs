using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace SoulLinkMod.Localization;

/// <summary>
/// Public localization API for Soul Link.
///
/// Strings live in the game's <see cref="LocManager"/> under the <see cref="Table"/>
/// name; source-of-truth JSON files are at <c>Localization/locales/{lang}.locjson</c> and
/// are merged into <c>LocManager</c> at runtime by <see cref="LocTableLoader"/>.
///
/// Usage:
///   <code>var s = Loc.T("settings.header");           // string</code>
///   <code>var ls = Loc.S("settings.header");          // LocString (for HoverTip etc.)</code>
///   <code>var s = Loc.Tf("debug.session_hp", hp, max);// formatted with args</code>
///
/// Key convention: lowercase, dot-separated, surface-prefixed:
///   <c>settings.hp_mode.shared_pool</c>, <c>tip.title</c>, <c>stats.damage_taken</c>.
/// </summary>
public static class Loc
{
    /// <summary>
    /// LocManager table we piggyback on. Cannot register a brand-new table from a mod
    /// (LocManager.GetTable throws on unknown names — _tables is private and gets fully
    /// replaced on SetLanguage). gameplay_ui exists in every locale and was already
    /// used by the original mod code, so we merge into it and namespace keys with
    /// <see cref="KeyPrefix"/> to avoid collisions with vanilla strings.
    /// </summary>
    public const string Table = "gameplay_ui";

    /// <summary>
    /// Prepended to every Soul Link key before lookup/merge. Keeps our keys from
    /// stomping vanilla <c>gameplay_ui</c> entries. JSON files store keys without
    /// this prefix; <see cref="LocTableLoader"/> adds it at merge time, and the
    /// resolve helpers below add it at read time.
    /// </summary>
    public const string KeyPrefix = "soullink.";

    /// <summary>Fallback locale code (matches the game's 3-letter convention).</summary>
    public const string FallbackLocale = "eng";

    /// <summary>
    /// The 14 language codes STS2 ships with — pulled verbatim from
    /// <c>LocManager.Languages</c>. Used to validate locale filenames at load time so
    /// a typo like <c>zh-CN.json</c> instead of <c>zhs.json</c> is logged loudly
    /// instead of silently never being picked up.
    /// </summary>
    public static readonly HashSet<string> KnownLocales = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "eng", "zhs", "deu", "esp", "fra", "ita", "jpn",
        "kor", "pol", "ptb", "rus", "spa", "tha", "tur",
    };

    /// <summary>
    /// Current game language code (3-letter, e.g. "eng", "zhs", "jpn"). Reads
    /// <see cref="LocManager.Language"/> directly. Falls back to "eng" if LocManager
    /// hasn't initialized yet.
    /// </summary>
    public static string CurrentLocale
        => LocManager.Instance?.Language ?? FallbackLocale;

    /// <summary>
    /// Resolve a key to the formatted string in the active locale.
    /// Falls back to the key itself if lookup throws (e.g. missing key).
    /// </summary>
    public static string T(string key)
    {
        LocTableLoader.EnsureLoaded();
        try
        {
            // LocString has no ToString() override — GetFormattedText() is the
            // SmartFormat resolve.
            return new LocString(Table, KeyPrefix + key).GetFormattedText();
        }
        catch { return key; }
    }

    /// <summary>
    /// Raw template fetch — bypasses SmartFormat. Use when the template contains
    /// <c>{0}, {1}, ...</c> positional placeholders that a caller-side parser
    /// (not SmartFormat) will expand. SmartFormat treats <c>{N}</c> as a named
    /// selector "N" and errors with "No source extension could handle selector
    /// named 'N'" when no source provides it.
    /// </summary>
    public static string TRaw(string key)
    {
        LocTableLoader.EnsureLoaded();
        try { return new LocString(Table, KeyPrefix + key).GetRawText(); }
        catch { return key; }
    }

    /// <summary>
    /// LocString form for APIs that want one (e.g. <c>HoverTip</c> title).
    /// Resolution is deferred — the consuming control re-resolves on each render,
    /// so a locale switch updates the displayed text without rebuilding the control.
    /// </summary>
    public static LocString S(string key)
    {
        LocTableLoader.EnsureLoaded();
        return new LocString(Table, KeyPrefix + key);
    }

    /// <summary>
    /// Resolve a key then <see cref="string.Format(string, object?[])"/> with the
    /// supplied args. JSON values use <c>{0}, {1}, ...</c> placeholders.
    ///
    /// Reads via <c>GetRawText()</c> instead of <c>GetFormattedText()</c> on purpose:
    /// SmartFormat treats <c>{0}</c> as a placeholder too, and with an empty variables
    /// dict it silently expands to empty. That turned BBCode like
    /// <c>[color=#{0}]+10[/color]</c> into <c>[color=#]+10[/color]</c>, which Godot
    /// then renders with the opening tag stripped (leaving a stray <c>]+10</c>).
    /// </summary>
    public static string Tf(string key, params object?[] args)
    {
        LocTableLoader.EnsureLoaded();
        string template;
        try { template = new LocString(Table, KeyPrefix + key).GetRawText(); }
        catch { template = key; }
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
