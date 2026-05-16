using Godot;
using SoulLinkMod.Localization;

namespace SoulLinkMod.UI;

/// <summary>
/// Lazy-loads the Soul Link UI font (Kreon Bold with a CJK fallback chain — see
/// <see cref="LocFontResolver"/>) and applies it to UI controls.
/// </summary>
internal static class SoulLinkFont
{
    private static Font? Font => LocFontResolver.Resolve();

    internal static void Apply(Label lbl)
    {
        var f = Font;
        if (f != null) lbl.AddThemeFontOverride("font", f);
    }

    internal static void Apply(Button btn)
    {
        var f = Font;
        if (f != null) btn.AddThemeFontOverride("font", f);
    }

    internal static void Apply(RichTextLabel rtl)
    {
        var f = Font;
        if (f != null) rtl.AddThemeFontOverride("normal_font", f);
    }

    internal static void Apply(PopupMenu popup)
    {
        var f = Font;
        if (f != null) popup.AddThemeFontOverride("font", f);
    }
}
