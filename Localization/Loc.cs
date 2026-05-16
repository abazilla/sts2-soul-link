using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Localization;

namespace SoulLinkMod.Localization;

/// <summary>
/// Public localization API for Soul Link.
///
/// Strings are stored in the game's <see cref="LocManager"/> tables (locale-switched by
/// the game). Source-of-truth files live under <c>Localization/locales/{locale}.json</c>
/// and are merged into <c>LocManager</c> at runtime by <see cref="LocTableLoader"/>.
///
/// Usage:
///   <code>var s = Loc.T("settings.header");           // string</code>
///   <code>var ls = Loc.S("settings.header");          // LocString (for HoverTip etc.)</code>
///
/// Key convention: lowercase, dot-separated, prefixed with the surface area:
///   <c>settings.hp_mode.shared_pool</c>, <c>tip.title</c>, <c>stats.damage_taken</c>.
/// </summary>
public static class Loc
{
    /// <summary>Name of the LocManager table all Soul Link keys are merged into.</summary>
    public const string Table = "soullink";

    /// <summary>Locale used as fallback when a key is missing from the active locale.</summary>
    public const string FallbackLocale = "en";

    /// <summary>
    /// Best-effort current locale code (e.g. "en", "zh-CN"). Falls back to "en" if the
    /// game's API is not reachable. Computed via reflection so the mod doesn't hard-bind
    /// to a specific LocManager API shape.
    /// </summary>
    public static string CurrentLocale
    {
        get
        {
            try
            {
                var inst = LocManager.Instance;
                if (inst == null) return FallbackLocale;
                var t = inst.GetType();
                var prop = t.GetProperty("CurrentLocale", BindingFlags.Instance | BindingFlags.Public)
                        ?? t.GetProperty("Locale",        BindingFlags.Instance | BindingFlags.Public)
                        ?? t.GetProperty("CurrentLanguage", BindingFlags.Instance | BindingFlags.Public);
                var raw = prop?.GetValue(inst)?.ToString();
                return string.IsNullOrEmpty(raw) ? FallbackLocale : raw!;
            }
            catch { return FallbackLocale; }
        }
    }

    /// <summary>
    /// Resolve a key to a plain string in the active locale. Returns the key itself
    /// if the lookup fails — callers should not see "LOC_KEY_MISSING" placeholders.
    /// </summary>
    public static string T(string key)
    {
        LocTableLoader.EnsureLoaded();
        try { return new LocString(Table, key).ToString() ?? key; }
        catch { return key; }
    }

    /// <summary>
    /// LocString form for APIs that want one (e.g. HoverTip title).
    /// Also ensures tables are loaded before returning.
    /// </summary>
    public static LocString S(string key)
    {
        LocTableLoader.EnsureLoaded();
        return new LocString(Table, key);
    }
}
