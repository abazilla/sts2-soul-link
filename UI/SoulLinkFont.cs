using Godot;

namespace SoulLinkMod.UI;

/// <summary>Lazy-loads Kreon Bold once and provides helpers to apply it to UI controls.</summary>
internal static class SoulLinkFont
{
    private static Font? _kreon;
    private static Font? Kreon => _kreon ??= ResourceLoader.Load<Font>("res://fonts/kreon_bold.ttf");

    internal static void Apply(Label lbl)
    {
        if (Kreon != null) lbl.AddThemeFontOverride("font", Kreon);
    }

    internal static void Apply(Button btn)
    {
        if (Kreon != null) btn.AddThemeFontOverride("font", Kreon);
    }

    internal static void Apply(RichTextLabel rtl)
    {
        if (Kreon != null) rtl.AddThemeFontOverride("normal_font", Kreon);
    }

    internal static void Apply(PopupMenu popup)
    {
        if (Kreon != null) popup.AddThemeFontOverride("font", Kreon);
    }
}
