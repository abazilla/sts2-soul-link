using System;
using Godot;

namespace SoulLinkMod.Localization;

/// <summary>
/// Resolves a UI font with a CJK fallback chain so non-Latin locales (zh-CN, zh-TW, ja, ko)
/// render real glyphs instead of tofu boxes.
///
/// Strategy:
///   1. Load Kreon Bold (Latin) as the primary font — keeps the existing look for English.
///   2. Probe a list of likely CJK font paths the game itself ships under <c>res://</c>.
///      STS2 supports Simplified Chinese, so it bundles a CJK font; we don't ship our own
///      until that probe is confirmed insufficient.
///   3. Attach the CJK font as a Godot <c>Font.Fallbacks</c> entry — Godot resolves any
///      missing glyph in the primary against the fallback chain transparently.
///
/// Override search paths via the <c>SOULLINK_CJK_FONT</c> environment variable for testing.
/// </summary>
internal static class LocFontResolver
{
    private static Font? _resolved;
    private static bool _resolveAttempted;

    /// <summary>Candidate paths probed in order for the CJK fallback.</summary>
    private static readonly string[] CjkCandidates =
    {
        // 1. Env override (testing/diagnostics)
        // 2. Likely game-bundled CJK fonts. These are guesses based on common STS2 layouts;
        //    update once the actual asset path is confirmed via GodotExplorer.
        "res://fonts/NotoSansSC-Bold.ttf",
        "res://fonts/NotoSansCJK-Bold.ttf",
        "res://fonts/SourceHanSansSC-Bold.otf",
        "res://Sts2/Assets/Fonts/NotoSansSC-Bold.ttf",
        // 3. Bundled fallback (next to the mod DLL) — only present if we eventually
        //    decide to ship our own. See Localization/README.md.
        "user://SoulLink/NotoSansSC-Bold.ttf",
    };

    /// <summary>The composite font Soul Link should apply to its UI controls.</summary>
    public static Font? Resolve()
    {
        if (_resolveAttempted) return _resolved;
        _resolveAttempted = true;

        try
        {
            var primary = ResourceLoader.Load<Font>("res://fonts/kreon_bold.ttf");
            if (primary == null)
            {
                SoulLinkLog.Error("LocFontResolver: primary font (Kreon Bold) failed to load.");
                return null;
            }

            var cjk = LoadCjkFallback();
            if (cjk != null)
            {
                try
                {
                    // FontFile/SystemFont expose a Fallbacks array. Defensive set via reflection
                    // so we don't blow up if the Godot type changes.
                    var prop = primary.GetType().GetProperty("Fallbacks");
                    if (prop != null && prop.CanWrite)
                    {
                        var arr = new Godot.Collections.Array<Font> { cjk };
                        prop.SetValue(primary, arr);
                    }
                }
                catch (Exception ex)
                {
                    SoulLinkLog.Error($"LocFontResolver: failed to attach CJK fallback: {ex.Message}");
                }
            }
            else
            {
                SoulLinkLog.Info("LocFontResolver: no CJK fallback found; non-Latin glyphs may render as tofu.");
            }

            _resolved = primary;
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"LocFontResolver.Resolve crashed: {ex}");
        }

        return _resolved;
    }

    private static Font? LoadCjkFallback()
    {
        var envOverride = System.Environment.GetEnvironmentVariable("SOULLINK_CJK_FONT");
        if (!string.IsNullOrEmpty(envOverride))
        {
            var f = TryLoad(envOverride!);
            if (f != null) return f;
        }

        foreach (var path in CjkCandidates)
        {
            var f = TryLoad(path);
            if (f != null)
            {
                SoulLinkLog.Info($"LocFontResolver: CJK fallback loaded from {path}");
                return f;
            }
        }
        return null;
    }

    private static Font? TryLoad(string path)
    {
        try
        {
            if (!ResourceLoader.Exists(path)) return null;
            return ResourceLoader.Load<Font>(path);
        }
        catch { return null; }
    }
}
