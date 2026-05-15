using System;
using Godot;

using SoulLinkMod;

namespace SoulLinkMod.UI;

/// <summary>
/// Collapsible run-stats panel showing cumulative damage taken, healing gained,
/// gold earned, and gold spent for the entire Soul Link run.
///
/// Added as a child of both combat and non-combat room nodes by RoomReadyPatch.
/// When collapsed, the content background is hidden; only the header button remains.
/// </summary>
public class RunStatsPanel : Control
{
    public static RunStatsPanel? Current;

    private const float PanelW = 210f;
    private const float PanelH = 160f;
    private const float AnchorV = 0.5f;
    private const float OffLeft = 10f;
    private const float OffTop  = -80f;

    private bool _expanded = true;
    private Button? _toggleButton;
    private PanelContainer? _bgPanel;
    private Label? _damageLbl;
    private Label? _healLbl;
    private Label? _goldEarnedLbl;
    private Label? _goldSpentLbl;

    private static readonly Color ColorDamage    = new(1f,   0.2f, 0.2f, 1f);
    private static readonly Color ColorHeal      = new(0.2f, 1f,   0.3f, 1f);
    private static readonly Color ColorGoldGain  = new(1f,   0.9f, 0f,   1f);
    private static readonly Color ColorGoldSpend = new(1f,   0.65f,0f,   1f);
    private static readonly Color ColorAccentYellow = new Color("efc851");

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        try
        {
            Current = this;

            AnchorLeft   = 0f;      AnchorRight  = 0f;
            AnchorTop    = AnchorV; AnchorBottom = AnchorV;
            OffsetLeft   = OffLeft;
            OffsetTop    = OffTop;
            OffsetRight  = OffLeft + PanelW;
            OffsetBottom = OffTop  + PanelH;

            // Ignore on the root so clicks outside the toggle button fall through to
            // game UI underneath (back button etc.). The header Button keeps its
            // default Stop filter, so the collapse toggle still works.
            MouseFilter = MouseFilterEnum.Ignore;

            BuildUI();
            Refresh();
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"RunStatsPanel.Initialize crashed: {ex}");
        }
    }

    public void Refresh()
    {
        try   { DoRefresh(); }
        catch (Exception ex) { SoulLinkLog.Error($"RunStatsPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(vbox);

        // Header button — borderless, looks like a plain clickable label.
        _toggleButton = new Button { Text = "Run Stats v" };
        _toggleButton.Alignment = HorizontalAlignment.Left;
        _toggleButton.Pressed += OnToggle;
        ApplyHeaderButtonStyle(_toggleButton);
        ApplyHeaderTextStyle(_toggleButton);
        SoulLinkFont.Apply(_toggleButton);
        vbox.AddChild(_toggleButton);

        // Content background — transparent (no panel bg).
        _bgPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _bgPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        vbox.AddChild(_bgPanel);

        var stats = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _bgPanel.AddChild(stats);

        _damageLbl     = MakeStatLabel();
        _healLbl       = MakeStatLabel();
        _goldEarnedLbl = MakeStatLabel();
        _goldSpentLbl  = MakeStatLabel();
        stats.AddChild(_damageLbl);
        stats.AddChild(_healLbl);
        stats.AddChild(_goldEarnedLbl);
        stats.AddChild(_goldSpentLbl);
    }

    private static Label MakeStatLabel()
    {
        var lbl = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        ApplyBodyTextStyle(lbl);
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    private static void ApplyHeaderTextStyle(Control c)
    {
        var hover = Lighten(ColorAccentYellow, 0.4f);
        c.AddThemeColorOverride("font_color",                ColorAccentYellow);
        c.AddThemeColorOverride("font_hover_color",          hover);
        c.AddThemeColorOverride("font_hover_pressed_color",  hover);
        c.AddThemeColorOverride("font_pressed_color",        hover);
        c.AddThemeColorOverride("font_focus_color",          ColorAccentYellow);
        c.AddThemeConstantOverride("outline_size", 6);
        c.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
    }

    private static Color Lighten(Color c, float t) =>
        new Color(c.R + (1f - c.R) * t, c.G + (1f - c.G) * t, c.B + (1f - c.B) * t, c.A);

    private static void ApplyBodyTextStyle(Control c)
    {
        c.AddThemeConstantOverride("shadow_offset_x", 1);
        c.AddThemeConstantOverride("shadow_offset_y", 1);
        c.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.85f));
    }

    private static void ApplyHeaderButtonStyle(Button btn)
    {
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal",   empty);
        btn.AddThemeStyleboxOverride("hover",    empty);
        btn.AddThemeStyleboxOverride("pressed",  empty);
        btn.AddThemeStyleboxOverride("focus",    empty);
        btn.AddThemeStyleboxOverride("disabled", empty);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        Visible = SoulLinkSession.IsActive;
        if (!Visible) return;

        if (_damageLbl     != null) SetStat(_damageLbl,     "Damage taken",   SoulLinkSession.TotalDamageTaken,   ColorDamage);
        if (_healLbl       != null) SetStat(_healLbl,       "Healing gained", SoulLinkSession.TotalHealingGained, ColorHeal);
        if (_goldEarnedLbl != null) SetStat(_goldEarnedLbl, "Gold earned",    SoulLinkSession.TotalGoldEarned,    ColorGoldGain);
        if (_goldSpentLbl  != null) SetStat(_goldSpentLbl,  "Gold spent",     SoulLinkSession.TotalGoldSpent,     ColorGoldSpend);

        if (_bgPanel != null)
            _bgPanel.Visible = _expanded;

        if (_toggleButton != null)
            _toggleButton.Text = _expanded ? "Run Stats v" : "Run Stats >";
    }

    private static void SetStat(Label lbl, string label, int value, Color color)
    {
        lbl.Text = $"{label}:  {value}";
        lbl.AddThemeColorOverride("font_color", color);
    }

    private void OnToggle()
    {
        _expanded = !_expanded;
        Refresh();
    }
}
