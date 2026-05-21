using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Localization;

namespace SoulLinkMod.Localization;

/// <summary>
/// Reads <c>Localization/locales/{lang}.locjson</c> files from disk (next to the mod DLL;
/// extension is .locjson, not .json, so STS2's recursive mod-manifest scanner skips them)
/// and merges them into the game's <see cref="LocManager"/> under the <see cref="Loc.Table"/>.
///
/// <para>Behavior:</para>
/// <list type="bullet">
///   <item><description>Lazy first-load via <see cref="EnsureLoaded"/> — safe to call from any
///   hot path; no-ops after success. Silently retries if <c>LocManager.Instance</c>
///   was null on a previous attempt (fixes the q60 NRE).</description></item>
///   <item><description>Subscribes to <c>LocManager.SubscribeToLocaleChange</c> on first
///   success so that switching the game's language re-merges Soul Link strings into
///   the freshly-loaded table. Game wipes <c>LocTable._translations</c> on locale
///   change — without this hook our keys would vanish.</description></item>
///   <item><description>Merge order: English (fallback) first, then the active locale on
///   top. Missing keys in the active locale fall through to English.</description></item>
/// </list>
///
/// File names must match the game's 3-letter language code (e.g. <c>eng.json</c>,
/// <c>zhs.json</c>, <c>jpn.json</c>). See <c>LocManager._weblateToGameLanguage</c>
/// for the full map.
/// </summary>
internal static class LocTableLoader
{
    private static readonly object _gate = new();
    private static bool _loaded;
    private static bool _subscribed;

    /// <summary>
    /// Try to populate LocManager with our keys for the active locale (+ English
    /// fallback). No-op once successful. Safe to call repeatedly.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            if (!TryMergeAllLocales()) return; // LocManager not ready yet — retry later
            _loaded = true;
            TrySubscribeLocaleChange();
        }
    }

    /// <summary>
    /// Locale-change callback: game has reloaded its LocTable for the new language,
    /// wiping our previously-merged keys. Re-merge so Soul Link strings reappear.
    /// </summary>
    private static void OnLocaleChanged()
    {
        try
        {
            TryMergeAllLocales();
            SoulLinkLog.Info($"Loc: re-merged after locale change -> {Loc.CurrentLocale}.");
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Loc: locale-change re-merge failed: {ex}");
        }
    }

    /// <returns>true if LocManager was available and the merge ran.</returns>
    private static bool TryMergeAllLocales()
    {
        try
        {
            var inst = LocManager.Instance;
            if (inst == null) return false;

            var dir = Path.Combine(ModDir(), "locales");
            if (!Directory.Exists(dir))
            {
                SoulLinkLog.Info($"Loc: no locales dir at {dir}; using key fallback.");
                return true;
            }

            var table = inst.GetTable(Loc.Table);
            if (table == null)
            {
                SoulLinkLog.Error($"Loc: LocManager.GetTable('{Loc.Table}') returned null.");
                return true;
            }

            var active = Loc.CurrentLocale;
            if (!Loc.KnownLocales.Contains(active))
                SoulLinkLog.Error($"Loc: active locale '{active}' is not in STS2's known 14-code list — strings may not resolve.");

            // Warn on stray locale files that don't match a real game code (typo guard).
            foreach (var file in Directory.GetFiles(dir, "*.locjson"))
            {
                var loc = Path.GetFileNameWithoutExtension(file);
                if (!Loc.KnownLocales.Contains(loc))
                    SoulLinkLog.Error($"Loc: locale file '{loc}.json' has no matching STS2 language code; rename to one of: eng/zhs/deu/esp/fra/ita/jpn/kor/pol/ptb/rus/spa/tha/tur.");
            }

            int merged = 0;
            // Merge English first (base), then active locale (overlay) so collisions
            // resolve in the active locale's favor and missing keys fall through to en.
            foreach (var file in OrderLocaleFiles(Directory.GetFiles(dir, "*.locjson"), active))
            {
                var locale = Path.GetFileNameWithoutExtension(file);
                // Skip locale files unrelated to the active session — they'd stomp
                // the overlay if loaded after.
                if (!IsRelevant(locale, active)) continue;

                try
                {
                    var map = ReadFlatJson(file);
                    // Namespace every Soul Link key so we don't stomp vanilla entries
                    // in the shared gameplay_ui table.
                    var prefixed = new Dictionary<string, string>(map.Count, StringComparer.Ordinal);
                    foreach (var kv in map) prefixed[Loc.KeyPrefix + kv.Key] = kv.Value;
                    table.MergeWith(prefixed);
                    merged++;
                }
                catch (Exception ex)
                {
                    SoulLinkLog.Error($"Loc: failed to load {file}: {ex.Message}");
                }
            }

            SoulLinkLog.Info($"Loc: merged {merged} file(s) into table '{Loc.Table}' (active='{active}').");
            return true;
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Loc: TryMergeAllLocales crashed: {ex}");
            return true; // don't spin — mark as attempted
        }
    }

    private static void TrySubscribeLocaleChange()
    {
        if (_subscribed) return;
        try
        {
            LocString.SubscribeToLocaleChange(OnLocaleChanged);
            _subscribed = true;
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"Loc: SubscribeToLocaleChange failed: {ex.Message}");
        }
    }

    private static bool IsRelevant(string locale, string active)
        => string.Equals(locale, active,             StringComparison.OrdinalIgnoreCase)
        || string.Equals(locale, Loc.FallbackLocale, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> OrderLocaleFiles(string[] files, string active)
    {
        int Rank(string f)
        {
            var locale = Path.GetFileNameWithoutExtension(f);
            if (string.Equals(locale, Loc.FallbackLocale, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(locale, active,             StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }
        Array.Sort(files, (a, b) => Rank(a).CompareTo(Rank(b)));
        return files;
    }

    private static string ModDir()
        => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

    /// <summary>Read a flat string->string JSON map. Nested objects are flattened with '.'.</summary>
    private static Dictionary<string, string> ReadFlatJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(doc.RootElement, prefix: "", dict);
        return dict;
    }

    private static void Walk(JsonElement el, string prefix, Dictionary<string, string> dict)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                // Skip top-level _meta and similar comment-only sections.
                if (p.Name.StartsWith("_")) continue;
                var key = string.IsNullOrEmpty(prefix) ? p.Name : $"{prefix}.{p.Name}";
                Walk(p.Value, key, dict);
            }
        }
        else if (el.ValueKind == JsonValueKind.String)
        {
            dict[prefix] = el.GetString() ?? "";
        }
        // numbers/bools/arrays are ignored — loc values are strings only
    }
}
