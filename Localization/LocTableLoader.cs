using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Localization;

namespace SoulLinkMod.Localization;

/// <summary>
/// Reads <c>Localization/locales/{locale}.json</c> files from disk (next to the mod DLL)
/// and merges them into the game's <see cref="LocManager"/> under the <see cref="Loc.Table"/>.
///
/// Lazy / idempotent: <see cref="EnsureLoaded"/> is safe to call from any code path —
/// it no-ops after first success and silently retries if <c>LocManager.Instance</c> was
/// null on a previous attempt (fixes the q60 NRE — see Loc README).
/// </summary>
internal static class LocTableLoader
{
    private static readonly object _gate = new();
    private static bool _loaded;

    /// <summary>
    /// Try to load every <c>locales/*.json</c> into LocManager. No-op once successful.
    /// Safe to call repeatedly and from hot paths (e.g. HoverTip postfix).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            try
            {
                var inst = LocManager.Instance;
                if (inst == null) return; // retry on next call (fixes q60)

                var dir = Path.Combine(ModDir(), "locales");
                if (!Directory.Exists(dir))
                {
                    SoulLinkLog.Info($"Loc: no locales dir at {dir}; using key fallback.");
                    _loaded = true;
                    return;
                }

                int loaded = 0;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    var locale = Path.GetFileNameWithoutExtension(file);
                    try
                    {
                        var map = ReadFlatJson(file);
                        MergeIntoLocManager(inst, locale, map);
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        SoulLinkLog.Error($"Loc: failed to load {file}: {ex.Message}");
                    }
                }

                _loaded = true;
                SoulLinkLog.Info($"Loc: merged {loaded} locale file(s) into '{Loc.Table}'.");
            }
            catch (Exception ex)
            {
                SoulLinkLog.Error($"Loc: EnsureLoaded crashed: {ex}");
            }
        }
    }

    private static string ModDir()
        => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

    /// <summary>Read a flat string->string JSON map. Nested keys are flattened with '.'.</summary>
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
                var key = string.IsNullOrEmpty(prefix) ? p.Name : $"{prefix}.{p.Name}";
                Walk(p.Value, key, dict);
            }
        }
        else if (el.ValueKind == JsonValueKind.String)
        {
            dict[prefix] = el.GetString() ?? "";
        }
        // ignore numbers/bools/arrays — loc values are strings only
    }

    /// <summary>
    /// Merge a flat key->value map into the game's LocManager. The game's table API
    /// historically used <c>GetTable(name).MergeWith(dict)</c>; we call that path via
    /// reflection so we don't hard-fail if the signature shifts between game versions.
    /// Per-locale merging is attempted if the API exposes it; otherwise we merge into
    /// the active table (game switches tables on locale change).
    /// </summary>
    private static void MergeIntoLocManager(LocManager inst, string locale, Dictionary<string, string> map)
    {
        var table = inst.GetTable(Loc.Table);
        if (table == null)
        {
            SoulLinkLog.Error($"Loc: LocManager.GetTable('{Loc.Table}') returned null for locale '{locale}'.");
            return;
        }

        // Try a locale-aware MergeWith(string locale, IDictionary<...>) first.
        var tType = table.GetType();
        var localized = tType.GetMethod("MergeWith", new[] { typeof(string), typeof(IDictionary<string, string>) });
        if (localized != null)
        {
            localized.Invoke(table, new object[] { locale, map });
            return;
        }

        // Fallback: only merge the active locale's strings into the unscoped table.
        if (string.Equals(locale, Loc.CurrentLocale, StringComparison.OrdinalIgnoreCase)
            || string.Equals(locale, Loc.FallbackLocale, StringComparison.OrdinalIgnoreCase))
        {
            table.MergeWith(map);
        }
    }
}
