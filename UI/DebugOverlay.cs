using System;
using Godot;
using MegaCrit.Sts2.Core.Runs;

using SoulLinkMod;
using SoulLinkMod.Localization;

namespace SoulLinkMod.UI;

/// <summary>
/// Live debug overlay showing two views of state side-by-side:
///
///   Session — what SoulLinkSession believes on this process (HP, Gold).
///   Game    — each player's actual Creature.CurrentHp right now.
///
/// Showing both reveals divergence: if the session HP doesn't match a player's
/// actual HP, the sync patch didn't fire on this process for that change (stale
/// session — displayed in orange).
///
/// Visible whenever a Soul Link session is active (combat and non-combat).
/// Collapsible. Polls via _Process so it stays current without sync-patch hooks.
/// When collapsed, the content background is hidden; only the header button remains.
/// </summary>
public class DebugOverlay : Control
{
    public static DebugOverlay? Current;

    private const float PanelW   = 240f;
    private const float PanelH   = 185f;
    private const float AnchorV  = 0.5f;
    private const float OffLeft  = 10f;
    private const float OffTop   = 90f;

    private bool _expanded = true;
    private Button? _toggleButton;
    private PanelContainer? _bgPanel;
    private Label? _sessionHpLbl;
    private Label? _sessionGoldLbl;
    private Label? _hpModeLbl;
    private Label? _splitMaxHpLbl;
    private Label? _splitHealLbl;
    private Label? _goldModeLbl;

    private static readonly Color ColorAccentYellow = new Color("efc851");
    private static readonly Color ColorHp           = new Color("4ec94e");
    private static readonly Color ColorGold         = ColorAccentYellow;
    private static readonly Color ColorBody         = new Color("e8e0c8");

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
            DoRefresh();
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"DebugOverlay.Initialize crashed: {ex}");
        }
    }

    public static void Clear() => Current = null;

    public void Refresh()
    {
        try { DoRefresh(); }
        catch (Exception ex) { SoulLinkLog.Error($"DebugOverlay.Refresh crashed: {ex}"); }
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(vbox);

        // Header button — borderless, looks like a plain clickable label.
        _toggleButton = new Button { Text = Loc.Tf("debug.header_expanded", SoulLinkMod.Version) };
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

        var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _bgPanel.AddChild(content);

        _hpModeLbl     = MakeLabel();
        _sessionHpLbl  = MakeLabel();
        _splitMaxHpLbl = MakeLabel();
        _splitHealLbl  = MakeLabel();
        _goldModeLbl   = MakeLabel();
        _sessionGoldLbl = MakeLabel();

        content.AddChild(_hpModeLbl);
        // Session HP + split flags hang under HP Mode.
        content.AddChild(IndentLabel(_sessionHpLbl,  20));
        content.AddChild(IndentLabel(_splitMaxHpLbl, 20));
        content.AddChild(IndentLabel(_splitHealLbl,  20));
        content.AddChild(MakeSpacer(2));
        content.AddChild(_goldModeLbl);
        content.AddChild(IndentLabel(_sessionGoldLbl, 20));
    }

    private static Control MakeSpacer(int height)
    {
        var c = new Control { MouseFilter = MouseFilterEnum.Ignore };
        c.CustomMinimumSize = new Vector2(0, height);
        return c;
    }

    private static Label MakeLabel()
    {
        var lbl = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        ApplyBodyTextStyle(lbl);
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    private static Control IndentLabel(Label lbl, int left)
    {
        var m = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        m.AddThemeConstantOverride("margin_left", left);
        m.AddChild(lbl);
        return m;
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

        var rs = SoulLinkSession.ActiveRunSettings;

        bool showSessionHp = rs.HpMode != HpMode.Vanilla;
        if (_sessionHpLbl?.GetParent() is Control sessHpWrap) sessHpWrap.Visible = showSessionHp;
        if (showSessionHp)
            Set(_sessionHpLbl, Loc.Tf("debug.session_hp", SoulLinkSession.CurrentHp, SoulLinkSession.MaxHp), ColorHp);

        bool showSessionGold = rs.GoldMode != GoldSharingMode.Default;
        if (_sessionGoldLbl?.GetParent() is Control sessGoldWrap) sessGoldWrap.Visible = showSessionGold;
        if (showSessionGold)
        {
            string goldLine = rs.GoldMode switch
            {
                GoldSharingMode.SplitByPlayer => Loc.Tf("debug.gold_per_player", SoulLinkSession.GetPlayerGold(0), SoulLinkSession.GetPlayerGold(1)),
                GoldSharingMode.SharedPool    => Loc.Tf("debug.session_gold", SoulLinkSession.Gold),
                _                             => "",
            };
            Set(_sessionGoldLbl, goldLine, ColorGold);
        }

        // Mode names: "Default" is translated; other enum members render their raw name
        // (English) for now — they're internal labels rarely seen in production debug.
        string hpModeName = rs.HpMode == HpMode.Vanilla ? Loc.T("debug.hp_mode_default") : rs.HpMode.ToString();
        Set(_hpModeLbl,     Loc.Tf("debug.hp_mode", hpModeName), ColorBody);

        // Split flags only apply when an HP-sharing mode is active.
        bool splitVisible = rs.HpMode != HpMode.Vanilla;
        if (_splitMaxHpLbl?.GetParent() is Control splitMaxWrap) splitMaxWrap.Visible = splitVisible;
        if (_splitHealLbl?.GetParent()  is Control splitHealWrap) splitHealWrap.Visible = splitVisible;
        if (splitVisible)
        {
            Set(_splitMaxHpLbl, Loc.Tf("debug.split_max_hp", rs.SplitMaxHp), ColorBody);
            Set(_splitHealLbl,  Loc.Tf("debug.split_heal",   rs.SplitHeal),  ColorBody);
        }

        Set(_goldModeLbl,   Loc.Tf("debug.gold_mode", rs.GoldMode), ColorBody);

        if (_bgPanel != null)
            _bgPanel.Visible = _expanded;

        if (_toggleButton != null)
            _toggleButton.Text = Loc.Tf(_expanded ? "debug.header_expanded" : "debug.header_collapsed", SoulLinkMod.Version);
    }

    private static void Set(Label? lbl, string text, Color color)
    {
        if (lbl == null) return;
        lbl.Text = text;
        lbl.AddThemeColorOverride("font_color", color);
    }

    private void OnToggle()
    {
        _expanded = !_expanded;
        DoRefresh();
    }
}
