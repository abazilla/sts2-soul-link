using System;
using Godot;

using SoulLinkMod;
using SoulLinkMod.Localization;

namespace SoulLinkMod.UI;

/// <summary>
/// Settings panel for the Soul Link mod. Injected into multiplayer lobby screens
/// (character select and custom run) via a CanvasLayer so it renders above the game UI.
///
/// RUN SETTINGS (top section): Host-editable before the run starts. Greyed out on
/// the client (read-only) and after the run begins. Changes are saved immediately.
///
/// PANEL SETTINGS (bottom section): Always local/editable regardless of role or
/// run state. Controls which overlay panels appear during a run.
/// </summary>
public class SoulLinkSettingsPanel : Control
{
    public static SoulLinkSettingsPanel? Current;

    // Panel dimensions
    private const float PanelW        = 400f;
    private const float RightMargin   = 16f;
    private const float TopMargin     = 16f;

    // Palette sampled from the vanilla STS2 character/relic tooltip card.
    private static readonly Color ColAccentYellow = new Color("efc851"); // gold-tinted accent
    private static readonly Color ColHpGreen      = new Color("4ec94e"); // HP green
    private static readonly Color ColOffWhite     = new Color("e8e0c8"); // descriptor body
    private static readonly Color ColHeader     = ColAccentYellow;
    private static readonly Color ColSection    = ColAccentYellow;
    private static readonly Color ColDisabled   = new Color("555555");
    private static readonly Color ColDescriptor = ColOffWhite;
    private static readonly Color ColBg         = new Color(0f, 0f, 0f, 0.72f);

    private bool _isClientMode; // true = client; run settings are read-only
    private bool _refreshing;   // suppresses signal handlers during DoRefresh
    private MarginContainer? _margin; // root layout container; drives panel height

    // Run setting controls
    private OptionButton? _hpModeOption;
    private CheckBox?     _cbAdditiveStartingHp;
    private OptionButton? _goldModeOption;
    private CheckBox?     _cbSharedLoseHp;

    // Panel visibility checkboxes
    private CheckBox? _cbShowCombatLog;
    private CheckBox? _cbShowRunStats;
    private CheckBox? _cbShowDebugOverlay;

    // Descriptor labels for run settings (need to gray out when disabled)
    private Label? _descHpMode;
    private RichTextLabel? _descAdditiveStartingHp;
    private Label? _descGoldMode;  // dynamic — text changes with selection

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    // Stable delegate ref so we can Unsubscribe with the same instance.
    private MegaCrit.Sts2.Core.Localization.LocManager.LocaleChangeCallback? _localeChangeCb;

    /// <summary>
    /// Called after the panel is added to a CanvasLayer. Builds the UI tree but leaves
    /// the panel hidden — call Show(isHost) once the lobby mode is known.
    /// </summary>
    public void Initialize()
    {
        try
        {
            Current = this;
            Visible = false;

            // Anchor to top-right corner; height is updated whenever content changes.
            AnchorLeft = AnchorRight = 1f;
            AnchorTop = AnchorBottom = 0f;
            OffsetRight  = -RightMargin;
            OffsetLeft   = -RightMargin - PanelW;
            OffsetTop    = TopMargin;
            OffsetBottom = TopMargin; // set properly after BuildUi via MinimumSizeChanged

            MouseFilter = MouseFilterEnum.Stop; // consume clicks so they don't fall through

            BuildUi();

            // Captured-string controls (Button.Text, OptionButton items, descriptors) don't
            // re-resolve on locale change — rebuild the subtree so labels pick up the new
            // language without forcing the user to reopen the panel.
            _localeChangeCb = OnLocaleChanged;
            LocaleBus.Subscribe(_localeChangeCb);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"SoulLinkSettingsPanel.Initialize crashed: {ex}");
        }
    }

    private void OnLocaleChanged()
    {
        try
        {
            // Tear down + rebuild. _isClientMode / _refreshing / Visible / Current
            // are fields, so they survive — only the Godot subtree is replaced.
            foreach (var child in GetChildren()) child.QueueFree();
            _margin = null;
            _hpModeOption = null; _cbAdditiveStartingHp = null;
            _goldModeOption = null; _cbSharedLoseHp = null;
            _cbShowCombatLog = null; _cbShowRunStats = null; _cbShowDebugOverlay = null;
            _descHpMode = null; _descAdditiveStartingHp = null; _descGoldMode = null;

            BuildUi();
            Refresh();
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"SoulLinkSettingsPanel.OnLocaleChanged crashed: {ex}");
        }
    }

    /// <summary>
    /// Show the panel and configure it for the current multiplayer role.
    /// Called after InitializeMultiplayerAsHost / InitializeMultiplayerAsClient fires.
    /// </summary>
    public void Show(bool isHost)
    {
        _isClientMode = !isHost;
        Visible = true;
        Refresh();
        SoulLinkLog.Debug($"[SyncDiag] Panel.Show isHost={isHost}");
        // Notify connected clients of current host settings as soon as the lobby is ready.
        if (isHost) SoulLinkSession.TrySendSettingsSync();
    }

    /// <summary>
    /// Updates all control states to match current settings and locks/unlocks controls.
    /// Called by SettingsSyncHandler on the client after host settings arrive.
    /// </summary>
    public void Refresh()
    {
        try { DoRefresh(); }
        catch (Exception ex) { SoulLinkLog.Error($"SoulLinkSettingsPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    public override void _ExitTree()
    {
        if (_localeChangeCb != null)
        {
            LocaleBus.Unsubscribe(_localeChangeCb);
            _localeChangeCb = null;
        }
        if (Current == this) Current = null;
    }

    // ── UI Construction ───────────────────────────────────────────────────────

    private void BuildUi()
    {
        _margin = new MarginContainer();
        _margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _margin.AddThemeConstantOverride("margin_left",   10);
        _margin.AddThemeConstantOverride("margin_right",  10);
        _margin.AddThemeConstantOverride("margin_top",    8);
        _margin.AddThemeConstantOverride("margin_bottom", 8);
        _margin.MinimumSizeChanged += UpdateHeight;
        AddChild(_margin);
        var margin = _margin;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        margin.AddChild(vbox);

        // Header
        var header = MakeLabel($"{Loc.T("settings.header")} (v{SoulLinkMod.Version})", ColHeader, 18);
        vbox.AddChild(header);
        vbox.AddChild(MakeSpacer(8));

        // ── Run Settings ──────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel(Loc.T("settings.hp_mode_label"), ColAccentYellow, 16));

        _hpModeOption = new OptionButton { FocusMode = FocusModeEnum.None };
        _hpModeOption.AddItem(Loc.T("settings.hp_mode.shared_pool"), (int)HpMode.SharedPool);
        _hpModeOption.AddItem(Loc.T("settings.hp_mode.vanilla"),     (int)HpMode.Vanilla);
        // _hpModeOption.AddItem("Shared HP & Block Pool",     (int)HpMode.SharedBlockSharedPool);
        _hpModeOption.AddThemeFontSizeOverride("font_size", 17);
        ApplyOptionButtonAccent(_hpModeOption, ColAccentYellow);
        SoulLinkFont.Apply(_hpModeOption);
        {
            var hpModePopup = _hpModeOption.GetPopup();
            SoulLinkFont.Apply(hpModePopup);
            hpModePopup.AddThemeFontSizeOverride("font_size", 17);
            // Disable SharedBlockSharedPool — not yet implemented.
            // int sbspIdx = _hpModeOption.GetItemIndex((int)HpMode.SharedBlockSharedPool);
            // if (sbspIdx >= 0)
            // {
            //     _hpModeOption.SetItemDisabled(sbspIdx, true);
            //     _hpModeOption.SetItemText(sbspIdx, "Shared HP & Block Pool  (coming soon)");
            // }
        }
        _hpModeOption.ItemSelected += idx =>
        {
            if (_refreshing) return;
            var mode = (HpMode)_hpModeOption.GetItemId((int)idx);
            SoulLinkLog.Debug($"[SyncDiag] Toggled HpMode={mode} isClient={_isClientMode}");
            if (mode == HpMode.SharedBlockSharedPool) { DoRefresh(); return; } // revert
            SoulLinkSettings.Instance.HpMode = mode;
            SoulLinkSettings.Save();
            if (!_isClientMode) SoulLinkSession.TrySendSettingsSync();
            if (_descHpMode != null) _descHpMode.Text = HpModeDescriptor(mode);
            DoRefresh();
        };
        vbox.AddChild(Indent(_hpModeOption));
        _descHpMode = MakeDescriptor(HpModeDescriptor(SoulLinkSettings.Instance.HpMode));
        vbox.AddChild(WrapDescriptor(_descHpMode));

        _cbAdditiveStartingHp = MakeCheckBox(Loc.T("settings.additive_starting_hp"));
        _cbAdditiveStartingHp.Toggled += on =>
        {
            if (_refreshing) return;
            SoulLinkLog.Debug($"[SyncDiag] Toggled StartingHpMode={(on ? StartingHpMode.Additive : StartingHpMode.Average)} isClient={_isClientMode}");
            SoulLinkSettings.Instance.StartingHpMode = on ? StartingHpMode.Additive : StartingHpMode.Average;
            SoulLinkSettings.Save();
            if (!_isClientMode) SoulLinkSession.TrySendSettingsSync();
        };
        vbox.AddChild(Indent(_cbAdditiveStartingHp));
        _descAdditiveStartingHp = MakeRichDescriptor(
            Loc.Tf("settings.additive_starting_hp_desc", ColHpGreen.ToHtml(false)));
        vbox.AddChild(WrapDescriptorControl(_descAdditiveStartingHp));

        vbox.AddChild(MakeSpacer(12));
        vbox.AddChild(MakeLabel(Loc.T("settings.gold_mode_label"), ColSection, 16));

        _goldModeOption = new OptionButton { FocusMode = FocusModeEnum.None };
        _goldModeOption.AddItem(Loc.T("settings.gold_mode.shared_pool"), (int)GoldSharingMode.SharedPool);
        _goldModeOption.AddItem(Loc.T("settings.gold_mode.vanilla"),     (int)GoldSharingMode.Default);
        // Turning this off for now - to add in the future
        // _goldModeOption.AddItem("Shared, Split Gold by players",    (int)GoldSharingMode.SplitByPlayer);
        _goldModeOption.AddThemeFontSizeOverride("font_size", 17);
        ApplyOptionButtonAccent(_goldModeOption, ColAccentYellow);
        SoulLinkFont.Apply(_goldModeOption);
        _goldModeOption.ItemSelected += idx =>
        {
            if (_refreshing) return;
            var mode = (GoldSharingMode)_goldModeOption.GetItemId((int)idx);
            SoulLinkLog.Debug($"[SyncDiag] Toggled GoldMode={mode} isClient={_isClientMode}");
            SoulLinkSettings.Instance.GoldMode = mode;
            SoulLinkSettings.Save();
            if (!_isClientMode) SoulLinkSession.TrySendSettingsSync();
            if (_descGoldMode != null) _descGoldMode.Text = GoldModeDescriptor(mode);
        };
        var popup = _goldModeOption.GetPopup();
        SoulLinkFont.Apply(popup);
        popup.AddThemeFontSizeOverride("font_size", 17);
        vbox.AddChild(Indent(_goldModeOption));
        _descGoldMode = MakeDescriptor(GoldModeDescriptor(SoulLinkSettings.Instance.GoldMode));
        vbox.AddChild(WrapDescriptor(_descGoldMode));

        // Turning this off - only valid for legacy networking, already works for VGQ
        // _cbSharedLoseHp = MakeCheckBox("Shared lose-HP effects");
        // _cbSharedLoseHp.Toggled += on => { SoulLinkLog.Debug($"[SyncDiag] Toggled SharedLoseHp={on} isClient={_isClientMode}"); SoulLinkSettings.Instance.SharedLoseHp = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        // vbox.AddChild(_cbSharedLoseHp);

        // ── Panel Settings ────────────────────────────────────────────────────
        vbox.AddChild(MakeSpacer(12));
        vbox.AddChild(MakeLabel(Loc.T("settings.panels_label"), ColSection, 16));

        _cbShowCombatLog = MakeCheckBox(Loc.T("settings.show_combat_log"));
        _cbShowCombatLog.Toggled += on => { SoulLinkSettings.Instance.ShowCombatLog = on; SoulLinkSettings.Save(); };
        vbox.AddChild(Indent(_cbShowCombatLog));
        vbox.AddChild(WrapDescriptor(MakeDescriptor(Loc.T("settings.show_combat_log_desc"))));

        _cbShowDebugOverlay = MakeCheckBox(Loc.T("settings.show_debug_overlay"));
        _cbShowDebugOverlay.Toggled += on => { SoulLinkSettings.Instance.ShowDebugOverlay = on; SoulLinkSettings.Save(); };
        vbox.AddChild(Indent(_cbShowDebugOverlay));
        vbox.AddChild(WrapDescriptor(MakeDescriptor(Loc.T("settings.show_debug_overlay_desc"))));

    }

    // ── Refresh logic ─────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        _refreshing = true;
        try { DoRefreshInner(); } finally { _refreshing = false; }
    }

    private void DoRefreshInner()
    {
        bool runActive   = SoulLinkSession.IsActive;
        bool runEditable = !_isClientMode && !runActive;
        SoulLinkLog.Debug($"[SyncDiag] DoRefreshInner: isClient={_isClientMode} runActive={runActive} pending={SoulLinkSession.PendingSyncedRunSettings.HasValue}");

        // Decide which values to display:
        // - Client during active run: show locked ActiveRunSettings from host.
        // - Client pre-run: show host's most-recent sync if we have one, else fall back to local.
        // - Host: show local SoulLinkSettings.
        bool sharedLoseHp;
        GoldSharingMode goldMode;
        HpMode hpMode;
        StartingHpMode startingHpMode;
        if (_isClientMode && runActive)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            goldMode     = rs.GoldMode;
            sharedLoseHp = rs.SharedLoseHp;
            hpMode       = rs.HpMode;
            startingHpMode = rs.StartingHpMode;
        }
        else if (_isClientMode && SoulLinkSession.PendingSyncedRunSettings.HasValue)
        {
            var rs = SoulLinkSession.PendingSyncedRunSettings.Value;
            goldMode     = rs.GoldMode;
            sharedLoseHp = rs.SharedLoseHp;
            hpMode       = rs.HpMode;
            startingHpMode = rs.StartingHpMode;
        }
        else
        {
            var s = SoulLinkSettings.Instance;
            goldMode     = s.GoldMode;
            sharedLoseHp = s.SharedLoseHp;
            hpMode       = s.HpMode;
            startingHpMode = s.StartingHpMode;
        }

        // HP-related settings are ignored when HpMode == Vanilla; grey them out.
        bool hpAxisEditable   = runEditable && hpMode != HpMode.Vanilla;
        // Gold settings are independent of HpMode; editable whenever the run is editable.
        bool goldAxisEditable = runEditable;
        // Client sees host's values read-only — render at full brightness so values stay
        // legible. Host (non-client) keeps the standard greyed-disabled look.
        Color lockedTint = _isClientMode ? Colors.White : ColDisabled;

        if (_hpModeOption != null)
        {
            int lmIdx = _hpModeOption.GetItemIndex((int)hpMode);
            if (lmIdx >= 0) _hpModeOption.Selected = lmIdx;
            _hpModeOption.Disabled = !runEditable;
            _hpModeOption.Modulate = runEditable
                ? Colors.White
                : new Color(lockedTint.R, lockedTint.G, lockedTint.B, 1f);
        }
        if (_descHpMode != null)
        {
            _descHpMode.Text = HpModeDescriptor(hpMode);
            SetDescriptor(_descHpMode, runEditable, lockedTint);
        }

        // Vanilla HpMode ignores all HP-axis settings — hide them entirely (host + client).
        bool hpAxisVisible = hpMode != HpMode.Vanilla;
        SetRowVisible(_cbAdditiveStartingHp, _descAdditiveStartingHp, hpAxisVisible);
        SetRowVisible(_cbSharedLoseHp, null,          hpAxisVisible);

        SetCheckBox(_cbAdditiveStartingHp, startingHpMode == StartingHpMode.Additive, hpAxisEditable, lockedTint);
        SetDescriptor(_descAdditiveStartingHp, hpAxisEditable, lockedTint);
        SetCheckBox(_cbSharedLoseHp,  sharedLoseHp, hpAxisEditable, lockedTint);

        if (_goldModeOption != null)
        {
            int itemIdx = _goldModeOption.GetItemIndex((int)goldMode);
            if (itemIdx >= 0) _goldModeOption.Selected = itemIdx;
            _goldModeOption.Disabled = !goldAxisEditable;
            _goldModeOption.Modulate = goldAxisEditable
                ? Colors.White
                : new Color(lockedTint.R, lockedTint.G, lockedTint.B, 1f);
        }
        if (_descGoldMode != null)
        {
            _descGoldMode.Text = GoldModeDescriptor(goldMode);
            SetDescriptor(_descGoldMode, goldAxisEditable, lockedTint);
        }

        // Panel settings are always locally editable.
        var ls = SoulLinkSettings.Instance;
        SetCheckBox(_cbShowCombatLog,    ls.ShowCombatLog,    editable: true,  lockedTint);
        SetCheckBox(_cbShowRunStats,     ls.ShowRunStats,     editable: true,  lockedTint);
        SetCheckBox(_cbShowDebugOverlay, ls.ShowDebugOverlay, editable: true,  lockedTint);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sets a checkbox's checked state and editable state without triggering the Toggled signal.</summary>
    private static void SetCheckBox(CheckBox? cb, bool value, bool editable, Color lockedTint)
    {
        if (cb == null) return;
        cb.SetPressedNoSignal(value);
        cb.Disabled = !editable;
        // Client lock (lockedTint == white) keeps yellow text legible but slightly dimmed.
        // Host run-lock uses the grey lockedTint at full alpha to read as disabled.
        bool clientLock = !editable && lockedTint.R >= 0.99f && lockedTint.G >= 0.99f && lockedTint.B >= 0.99f;
        cb.Modulate = editable
            ? Colors.White
            : clientLock
                ? new Color(1f, 1f, 1f, 0.75f)
                : new Color(lockedTint.R, lockedTint.G, lockedTint.B, 1f);
    }

    private Label MakeLabel(string text, Color color, int fontSize = 17)
    {
        var lbl = new Label
        {
            Text         = text,
            Modulate     = color,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        // Black outline around heading text for readability over arbitrary backgrounds.
        lbl.AddThemeConstantOverride("outline_size", 6);
        lbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    private static readonly Color ColCheckOutline = ColAccentYellow;

    private CheckBox MakeCheckBox(string text)
    {
        var cb = new CheckBox
        {
            Text       = text,
            FocusMode  = FocusModeEnum.None,
        };
        cb.AddThemeFontSizeOverride("font_size", 17);
        TintCheckBox(cb, ColAccentYellow);
        // Black text outline so checkbox labels read over arbitrary backgrounds.
        cb.AddThemeConstantOverride("outline_size", 6);
        cb.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
        ApplyCheckBoxOutline(cb);
        SoulLinkFont.Apply(cb);
        return cb;
    }

    /// <summary>Tints the OptionButton's main text and popup items.</summary>
    private static void ApplyOptionButtonAccent(OptionButton btn, Color color)
    {
        var outline = new Color(0f, 0f, 0f, 0.95f);
        btn.AddThemeColorOverride("font_color",         color);
        btn.AddThemeColorOverride("font_hover_color",   color);
        btn.AddThemeColorOverride("font_focus_color",   color);
        btn.AddThemeColorOverride("font_pressed_color", color);
        btn.AddThemeConstantOverride("outline_size", 6);
        btn.AddThemeColorOverride("font_outline_color", outline);
        var popup = btn.GetPopup();
        popup.AddThemeColorOverride("font_color",          color);
        popup.AddThemeColorOverride("font_hover_color",    color);
        popup.AddThemeColorOverride("font_focus_color",    color);
        popup.AddThemeColorOverride("font_disabled_color", new Color(color.R, color.G, color.B, 0.5f));
        popup.AddThemeConstantOverride("outline_size", 6);
        popup.AddThemeColorOverride("font_outline_color", outline);
    }

    /// <summary>Tints a CheckBox label color (all interaction states).</summary>
    private static void TintCheckBox(CheckBox cb, Color color)
    {
        // Brighten hover/pressed text so the highlight is visible regardless of toggle.
        // Without this the hover state is identical to normal and nothing changes on
        // hover when the box is unchecked (icon also doesn't swap).
        var hover = Lighten(color, 0.4f);
        cb.AddThemeColorOverride("font_color",          color);
        cb.AddThemeColorOverride("font_hover_color",    hover);
        cb.AddThemeColorOverride("font_hover_pressed_color", hover);
        cb.AddThemeColorOverride("font_pressed_color",  hover);
        cb.AddThemeColorOverride("font_focus_color",    color);
        cb.AddThemeColorOverride("font_disabled_color", new Color(color.R, color.G, color.B, 0.55f));
    }

    private static Color Lighten(Color c, float t) =>
        new Color(c.R + (1f - c.R) * t, c.G + (1f - c.G) * t, c.B + (1f - c.B) * t, c.A);

    /// <summary>
    /// Adds a yellow outline border around the CheckBox indicator while preserving
    /// the vanilla tick artwork. We fetch the theme's existing checked/unchecked
    /// textures and composite each onto a transparent canvas with a yellow border
    /// painted around the perimeter.
    /// </summary>
    private static void ApplyCheckBoxOutline(CheckBox cb)
    {
        foreach (var name in new[] {
            "checked", "unchecked",
            "checked_disabled", "unchecked_disabled",
            "radio_checked", "radio_unchecked",
            "radio_checked_disabled", "radio_unchecked_disabled",
        })
        {
            var src = cb.GetThemeIcon(name, "CheckBox");
            if (src == null) continue;
            var outlined = BuildOutlinedIcon(src);
            if (outlined != null) cb.AddThemeIconOverride(name, outlined);
        }
    }

    private static ImageTexture? BuildOutlinedIcon(Texture2D src)
    {
        try
        {
            var srcImg = src.GetImage();
            if (srcImg == null) return null;
            if (srcImg.GetFormat() != Image.Format.Rgba8)
                srcImg.Convert(Image.Format.Rgba8);

            int w = srcImg.GetWidth();
            int h = srcImg.GetHeight();
            const int border = 1;
            const int inset  = 2; // shrinks the tick area inside the canvas

            // Output canvas same size as vanilla — visual box becomes (inset*2) smaller.
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            img.Fill(new Color(0, 0, 0, 0));

            // Scale vanilla tick into the inset region.
            int innerW = System.Math.Max(1, w - inset * 2);
            int innerH = System.Math.Max(1, h - inset * 2);
            var scaled = (Image)srcImg.Duplicate();
            scaled.Resize(innerW, innerH, Image.Interpolation.Bilinear);
            img.BlitRect(scaled, new Rect2I(0, 0, innerW, innerH), new Vector2I(inset, inset));

            // 1px yellow border around the inset box.
            int x0 = inset - border;
            int y0 = inset - border;
            int x1 = w - inset + border - 1;
            int y1 = h - inset + border - 1;
            for (int x = x0; x <= x1; x++)
            {
                if (x >= 0 && x < w)
                {
                    if (y0 >= 0)     img.SetPixel(x, y0, ColCheckOutline);
                    if (y1 < h)      img.SetPixel(x, y1, ColCheckOutline);
                }
            }
            for (int y = y0; y <= y1; y++)
            {
                if (y >= 0 && y < h)
                {
                    if (x0 >= 0)     img.SetPixel(x0, y, ColCheckOutline);
                    if (x1 < w)      img.SetPixel(x1, y, ColCheckOutline);
                }
            }
            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"BuildOutlinedIcon failed: {ex.Message}");
            return null;
        }
    }

    private const int IndentPx           = 16;
    // = IndentPx + indicator width + h_separation, so descriptor lines up under the
    // checkbox label text instead of under the box itself.
    private const int DescriptorIndentPx = 40;

    /// <summary>Wraps a control in a MarginContainer with a fixed left indent.</summary>
    private static MarginContainer Indent(Control c, int left = IndentPx)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", left);
        m.AddChild(c);
        return m;
    }

    private static MarginContainer WrapDescriptor(Label lbl) => WrapDescriptorControl(lbl);

    private static MarginContainer WrapDescriptorControl(Control c) => Indent(c, DescriptorIndentPx);

    private static void ApplyDescriptorShadow(Control c)
    {
        // Drop shadow under descriptor body text for readability without a panel bg.
        c.AddThemeConstantOverride("shadow_offset_x", 1);
        c.AddThemeConstantOverride("shadow_offset_y", 1);
        c.AddThemeConstantOverride("shadow_outline_size", 0);
        c.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.85f));
    }

    private Label MakeDescriptor(string text)
    {
        var lbl = new Label
        {
            Text         = text,
            Modulate     = ColDescriptor,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        lbl.AddThemeFontSizeOverride("font_size", 15);
        ApplyDescriptorShadow(lbl);
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    /// <summary>
    /// Returns a RichTextLabel for descriptor lines that need per-segment color via
    /// BBCode (e.g. green "+10" / "Healing" inside off-white prose). The Modulate is
    /// kept white so BBCode colors render true; SetDescriptor still works to grey it.
    /// </summary>
    private RichTextLabel MakeRichDescriptor(string bbcode)
    {
        var lbl = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent    = true,
            ScrollActive  = false,
            AutowrapMode  = TextServer.AutowrapMode.WordSmart,
            MouseFilter   = MouseFilterEnum.Ignore,
        };
        lbl.AddThemeFontSizeOverride("normal_font_size", 15);
        lbl.AddThemeColorOverride("default_color", ColDescriptor);
        ApplyDescriptorShadow(lbl);
        SoulLinkFont.Apply(lbl);
        lbl.Text = bbcode;
        return lbl;
    }

    private static void SetDescriptor(Label? lbl, bool editable, Color lockedTint)
    {
        if (lbl == null) return;
        lbl.Modulate = editable ? ColDescriptor : new Color(lockedTint.R, lockedTint.G, lockedTint.B, 0.85f);
    }

    private static void SetDescriptor(RichTextLabel? lbl, bool editable, Color lockedTint)
    {
        if (lbl == null) return;
        // BBCode segments carry their own colors — when editable, keep Modulate white
        // so green stays green. When locked, multiply by lockedTint to grey the whole row.
        lbl.Modulate = editable ? Colors.White : new Color(lockedTint.R, lockedTint.G, lockedTint.B, 0.85f);
    }

    /// <summary>
    /// Toggles visibility of a checkbox + (optional) descriptor wrapper as a unit. The
    /// descriptor lives in a MarginContainer parent — hide the parent so the row
    /// collapses with no empty margin.
    /// </summary>
    private static void SetRowVisible(CheckBox? cb, Control? descriptor, bool visible)
    {
        // Both controls live inside a MarginContainer (indent / descriptor wrapper). Hide
        // the wrapper itself so the VBox collapses cleanly instead of leaving margins.
        if (cb?.GetParent() is Control cbWrapper) cbWrapper.Visible = visible;
        else if (cb != null) cb.Visible = visible;
        if (descriptor?.GetParent() is Control descWrapper) descWrapper.Visible = visible;
    }

    private void UpdateHeight()
    {
        if (_margin == null) return;
        OffsetBottom = OffsetTop + _margin.GetCombinedMinimumSize().Y;
    }

    private static string HpModeDescriptor(HpMode mode) => mode switch
    {
        HpMode.Vanilla                => Loc.T("settings.hp_mode_desc.vanilla"),
        HpMode.SharedBlockSharedPool  => Loc.T("settings.hp_mode_desc.coming_soon"),
        _                             => Loc.T("settings.hp_mode_desc.shared_pool"),
    };

    private static string GoldModeDescriptor(GoldSharingMode mode) => mode switch
    {
        GoldSharingMode.SharedPool    => Loc.T("settings.gold_mode_desc.shared_pool"),
        GoldSharingMode.SplitByPlayer => Loc.T("settings.gold_mode_desc.split"),
        _                             => Loc.T("settings.gold_mode_desc.vanilla"),
    };

    private static Control MakeSpacer(int height)
    {
        var c = new Control();
        c.CustomMinimumSize = new Vector2(0, height);
        c.MouseFilter = MouseFilterEnum.Ignore;
        return c;
    }

}
