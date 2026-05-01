using System;
using Godot;

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

    // Colours (matching the rest of the mod's palette)
    private static readonly Color ColHeader     = new Color("cccccc");
    private static readonly Color ColSection    = new Color("888888");
    private static readonly Color ColDisabled   = new Color("555555");
    private static readonly Color ColDescriptor = new Color("666666");
    private static readonly Color ColBg         = new Color(0f, 0f, 0f, 0.72f);

    private bool _isClientMode; // true = client; run settings are read-only
    private bool _refreshing;   // suppresses signal handlers during DoRefresh
    private MarginContainer? _margin; // root layout container; drives panel height

    // Run setting controls
    private CheckBox?     _cbSplitMaxHp;
    private CheckBox?     _cbSplitHeal;
    private OptionButton? _goldModeOption;

    // Panel visibility checkboxes
    private CheckBox? _cbShowCombatLog;
    private CheckBox? _cbShowRunStats;
    private CheckBox? _cbShowDebugOverlay;

    // Descriptor labels for run settings (need to gray out when disabled)
    private Label? _descSplitMaxHp;
    private Label? _descSplitHeal;
    private Label? _descGoldMode;  // dynamic — text changes with selection

    // ── Lifecycle ─────────────────────────────────────────────────────────────

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
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] SoulLinkSettingsPanel.Initialize crashed: {ex}");
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
        catch (Exception ex) { GD.PrintErr($"[SoulLink] SoulLinkSettingsPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    // ── UI Construction ───────────────────────────────────────────────────────

    private void BuildUi()
    {
        // Semi-transparent background
        var bg = new ColorRect { Color = ColBg };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);

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
        var header = MakeLabel($"SOUL LINK  v{SoulLinkMod.Version}", ColHeader, 18);
        vbox.AddChild(header);
        vbox.AddChild(MakeSeparator());

        // ── Run Settings ──────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel("  RUN SETTINGS  (host only)", ColSection, 16));

        _cbSplitMaxHp = MakeCheckBox("Split MaxHP changes by players");
        _cbSplitMaxHp.Toggled += on => { if (_refreshing) return; SoulLinkSettings.Instance.SplitMaxHp = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        vbox.AddChild(_cbSplitMaxHp);
        _descSplitMaxHp = MakeDescriptor("eg +10 MaxHP -> +5 MaxHP to the MaxHP pool (for 2 players)");
        vbox.AddChild(WrapDescriptor(_descSplitMaxHp));

        _cbSplitHeal = MakeCheckBox("Split healing by players");
        _cbSplitHeal.Toggled += on => { if (_refreshing) return; SoulLinkSettings.Instance.SplitHeal = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        vbox.AddChild(_cbSplitHeal);
        _descSplitHeal = MakeDescriptor("eg +10 Healing -> +5 Healing to the shared HP pool (for 2 players)");
        vbox.AddChild(WrapDescriptor(_descSplitHeal));

        vbox.AddChild(MakeLabel("  GOLD MODE:", ColSection, 16));

        _goldModeOption = new OptionButton { FocusMode = FocusModeEnum.None };
        _goldModeOption.AddItem("Default (STS2 native)",    (int)GoldSharingMode.Default);
        _goldModeOption.AddItem("Share gold pool",          (int)GoldSharingMode.SharedPool);
        _goldModeOption.AddItem("Split gold by players",    (int)GoldSharingMode.SplitByPlayer);
        _goldModeOption.AddThemeFontSizeOverride("font_size", 17);
        SoulLinkFont.Apply(_goldModeOption);
        _goldModeOption.ItemSelected += idx =>
        {
            if (_refreshing) return;
            var mode = (GoldSharingMode)_goldModeOption.GetItemId((int)idx);
            SoulLinkSettings.Instance.GoldMode = mode;
            SoulLinkSettings.Save();
            if (!_isClientMode) SoulLinkSession.TrySendSettingsSync();
            if (_descGoldMode != null) _descGoldMode.Text = GoldModeDescriptor(mode);
        };
        var popup = _goldModeOption.GetPopup();
        SoulLinkFont.Apply(popup);
        popup.AddThemeFontSizeOverride("font_size", 17);
        vbox.AddChild(_goldModeOption);
        _descGoldMode = MakeDescriptor(GoldModeDescriptor(SoulLinkSettings.Instance.GoldMode));
        vbox.AddChild(WrapDescriptor(_descGoldMode));

        vbox.AddChild(MakeSeparator());

        // ── Panel Settings ────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel("  PANELS  (local)", ColSection, 16));

        _cbShowCombatLog = MakeCheckBox("Show Soul Link Feed");
        _cbShowCombatLog.Toggled += on => { SoulLinkSettings.Instance.ShowCombatLog = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowCombatLog);
        vbox.AddChild(WrapDescriptor(MakeDescriptor("Show recent events")));

        _cbShowRunStats = MakeCheckBox("Show Run Stats");
        _cbShowRunStats.Toggled += on => { SoulLinkSettings.Instance.ShowRunStats = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowRunStats);
        vbox.AddChild(WrapDescriptor(MakeDescriptor("Panel showing stats for all players")));

        _cbShowDebugOverlay = MakeCheckBox("Show Debug Overlay");
        _cbShowDebugOverlay.Toggled += on => { SoulLinkSettings.Instance.ShowDebugOverlay = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowDebugOverlay);
        vbox.AddChild(WrapDescriptor(MakeDescriptor("Developer info \u2014 safe to leave off")));

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

        // Decide which values to display:
        // - Client during active run: show locked ActiveRunSettings from host.
        // - Everyone else: show current SoulLinkSettings.
        bool splitMaxHp, splitHeal;
        GoldSharingMode goldMode;
        if (_isClientMode && runActive)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            splitMaxHp = rs.SplitMaxHp;
            splitHeal  = rs.SplitHeal;
            goldMode   = rs.GoldMode;
        }
        else
        {
            var s = SoulLinkSettings.Instance;
            splitMaxHp = s.SplitMaxHp;
            splitHeal  = s.SplitHeal;
            goldMode   = s.GoldMode;
        }

        SetCheckBox(_cbSplitMaxHp, splitMaxHp, runEditable);
        SetDescriptor(_descSplitMaxHp, runEditable);
        SetCheckBox(_cbSplitHeal,  splitHeal,  runEditable);
        SetDescriptor(_descSplitHeal, runEditable);

        if (_goldModeOption != null)
        {
            int itemIdx = _goldModeOption.GetItemIndex((int)goldMode);
            if (itemIdx >= 0) _goldModeOption.Selected = itemIdx;
            _goldModeOption.Disabled = !runEditable;
            _goldModeOption.Modulate = runEditable
                ? Colors.White
                : new Color(ColDisabled.R, ColDisabled.G, ColDisabled.B, 1f);
        }
        if (_descGoldMode != null)
        {
            _descGoldMode.Text = GoldModeDescriptor(goldMode);
            SetDescriptor(_descGoldMode, runEditable);
        }

        // Panel settings are always locally editable.
        var ls = SoulLinkSettings.Instance;
        SetCheckBox(_cbShowCombatLog,    ls.ShowCombatLog,    editable: true);
        SetCheckBox(_cbShowRunStats,     ls.ShowRunStats,     editable: true);
        SetCheckBox(_cbShowDebugOverlay, ls.ShowDebugOverlay, editable: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Sets a checkbox's checked state and editable state without triggering the Toggled signal.</summary>
    private static void SetCheckBox(CheckBox? cb, bool value, bool editable)
    {
        if (cb == null) return;
        cb.SetPressedNoSignal(value);
        cb.Disabled = !editable;
        cb.Modulate = editable ? Colors.White : new Color(ColDisabled.R, ColDisabled.G, ColDisabled.B, 1f);
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
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    private CheckBox MakeCheckBox(string text)
    {
        var cb = new CheckBox
        {
            Text       = text,
            FocusMode  = FocusModeEnum.None,
        };
        cb.AddThemeFontSizeOverride("font_size", 17);
        SoulLinkFont.Apply(cb);
        return cb;
    }

    private static MarginContainer WrapDescriptor(Label lbl)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", 20);
        m.AddChild(lbl);
        return m;
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
        SoulLinkFont.Apply(lbl);
        return lbl;
    }

    private static void SetDescriptor(Label? lbl, bool editable)
    {
        if (lbl == null) return;
        lbl.Modulate = editable ? ColDescriptor : new Color(ColDisabled.R, ColDisabled.G, ColDisabled.B, 0.7f);
    }

    private void UpdateHeight()
    {
        if (_margin == null) return;
        OffsetBottom = OffsetTop + _margin.GetCombinedMinimumSize().Y;
    }

    private static string GoldModeDescriptor(GoldSharingMode mode) => mode switch
    {
        GoldSharingMode.SharedPool    => "Gold earned AND spent is taken from the shared gold pool",
        GoldSharingMode.SplitByPlayer => "Gold earned is split, but is spent individually",
        _                             => "Each player earns and spends gold on their own",
    };

    private static HSeparator MakeSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separation_color", new Color("333333"));
        return sep;
    }


}
