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
    private const float PanelW        = 300f;
    private const float RightMargin   = 16f;
    private const float TopMargin     = 16f;

    // Colours (matching the rest of the mod's palette)
    private static readonly Color ColHeader   = new Color("cccccc");
    private static readonly Color ColSection  = new Color("888888");
    private static readonly Color ColDisabled = new Color("555555");
    private static readonly Color ColBg       = new Color(0f, 0f, 0f, 0.72f);

    private bool _isClientMode; // true = client; run settings are read-only

    // Run setting controls
    private CheckBox?     _cbSplitMaxHp;
    private CheckBox?     _cbSplitHeal;
    private OptionButton? _goldModeOption;
    private CheckBox?     _cbSharedLoseHp;

    // Panel visibility checkboxes
    private CheckBox? _cbShowCombatLog;
    private CheckBox? _cbShowRunStats;
    private CheckBox? _cbShowDebugOverlay;

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

            // Anchor to top-right corner.
            AnchorLeft = AnchorRight = 1f;
            AnchorTop = AnchorBottom = 0f;
            OffsetRight  = -RightMargin;
            OffsetLeft   = -RightMargin - PanelW;
            OffsetTop    = TopMargin;
            OffsetBottom = TopMargin + EstimatedHeight();

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

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left",   10);
        margin.AddThemeConstantOverride("margin_right",  10);
        margin.AddThemeConstantOverride("margin_top",    8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        margin.AddChild(vbox);

        // Header
        var header = MakeLabel($"SOUL LINK  v{SoulLinkMod.Version}", ColHeader, 15);
        vbox.AddChild(header);
        vbox.AddChild(MakeSeparator());

        // ── Run Settings ──────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel("  RUN SETTINGS  (host only)", ColSection, 12));

        _cbSplitMaxHp = MakeCheckBox("Split MaxHP changes by players");
        _cbSplitMaxHp.Toggled += on => { SoulLinkSettings.Instance.SplitMaxHp = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        vbox.AddChild(_cbSplitMaxHp);

        _cbSplitHeal = MakeCheckBox("Split healing by players");
        _cbSplitHeal.Toggled += on => { SoulLinkSettings.Instance.SplitHeal = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        vbox.AddChild(_cbSplitHeal);

        vbox.AddChild(MakeLabel("  Gold mode:", ColSection, 12));

        _goldModeOption = new OptionButton { FocusMode = FocusModeEnum.None };
        _goldModeOption.AddItem("Default (STS2 native)",    (int)GoldSharingMode.Default);
        _goldModeOption.AddItem("Share gold pool",          (int)GoldSharingMode.SharedPool);
        _goldModeOption.AddItem("Split gold by players",    (int)GoldSharingMode.SplitByPlayer);
        _goldModeOption.AddThemeFontSizeOverride("font_size", 13);
        SoulLinkFont.Apply(_goldModeOption);
        _goldModeOption.ItemSelected += idx =>
        {
            SoulLinkSettings.Instance.GoldMode = (GoldSharingMode)_goldModeOption.GetItemId((int)idx);
            SoulLinkSettings.Save();
            if (!_isClientMode) SoulLinkSession.TrySendSettingsSync();
        };
        var popup = _goldModeOption.GetPopup();
        SoulLinkFont.Apply(popup);
        popup.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_goldModeOption);

        _cbSharedLoseHp = MakeCheckBox("Shared lose-HP effects");
        _cbSharedLoseHp.Toggled += on => { SoulLinkSettings.Instance.SharedLoseHp = on; SoulLinkSettings.Save(); if (!_isClientMode) SoulLinkSession.TrySendSettingsSync(); };
        vbox.AddChild(_cbSharedLoseHp);

        vbox.AddChild(MakeSeparator());

        // ── Panel Settings ────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel("  PANELS  (local)", ColSection, 12));

        _cbShowCombatLog = MakeCheckBox("Show Soul Link Feed");
        _cbShowCombatLog.Toggled += on => { SoulLinkSettings.Instance.ShowCombatLog = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowCombatLog);

        _cbShowRunStats = MakeCheckBox("Show Run Stats");
        _cbShowRunStats.Toggled += on => { SoulLinkSettings.Instance.ShowRunStats = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowRunStats);

        _cbShowDebugOverlay = MakeCheckBox("Show Debug Overlay");
        _cbShowDebugOverlay.Toggled += on => { SoulLinkSettings.Instance.ShowDebugOverlay = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbShowDebugOverlay);

        // Size the panel now that we know content height.
        OffsetBottom = OffsetTop + EstimatedHeight();
    }

    // ── Refresh logic ─────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        bool runActive   = SoulLinkSession.IsActive;
        bool runEditable = !_isClientMode && !runActive;

        // Decide which values to display:
        // - Client during active run: show locked ActiveRunSettings from host.
        // - Everyone else: show current SoulLinkSettings.
        bool splitMaxHp, splitHeal, sharedLoseHp;
        GoldSharingMode goldMode;
        if (_isClientMode && runActive)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            splitMaxHp   = rs.SplitMaxHp;
            splitHeal    = rs.SplitHeal;
            goldMode     = rs.GoldMode;
            sharedLoseHp = rs.SharedLoseHp;
        }
        else
        {
            var s = SoulLinkSettings.Instance;
            splitMaxHp   = s.SplitMaxHp;
            splitHeal    = s.SplitHeal;
            goldMode     = s.GoldMode;
            sharedLoseHp = s.SharedLoseHp;
        }

        SetCheckBox(_cbSplitMaxHp,    splitMaxHp,   runEditable);
        SetCheckBox(_cbSplitHeal,     splitHeal,    runEditable);
        SetCheckBox(_cbSharedLoseHp,  sharedLoseHp, runEditable);

        if (_goldModeOption != null)
        {
            int itemIdx = _goldModeOption.GetItemIndex((int)goldMode);
            if (itemIdx >= 0) _goldModeOption.Selected = itemIdx;
            _goldModeOption.Disabled = !runEditable;
            _goldModeOption.Modulate = runEditable
                ? Colors.White
                : new Color(ColDisabled.R, ColDisabled.G, ColDisabled.B, 1f);
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

    private Label MakeLabel(string text, Color color, int fontSize = 13)
    {
        var lbl = new Label
        {
            Text              = text,
            Modulate          = color,
            AutowrapMode      = TextServer.AutowrapMode.Off,
            ClipText          = false,
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
        cb.AddThemeFontSizeOverride("font_size", 13);
        SoulLinkFont.Apply(cb);
        return cb;
    }

    private static HSeparator MakeSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("separation_color", new Color("333333"));
        return sep;
    }

    /// <summary>
    /// Rough height estimate: 1 header + 1 sep + 1 section + 2 checkboxes + 1 gold label +
    /// 1 dropdown + 1 shared-lose-hp checkbox + 1 sep + 1 section + 3 checkboxes = 13 items × ~24px + 16px margins.
    /// </summary>
    private static float EstimatedHeight() => 13 * 24f + 16f;
}
