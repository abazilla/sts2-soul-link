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
    private const float PanelW       = 300f;
    private const float LeftMargin   = 16f;
    private const float BottomMargin = 16f;

    // Colours (matching the rest of the mod's palette)
    private static readonly Color ColHeader   = new Color("cccccc");
    private static readonly Color ColSection  = new Color("888888");
    private static readonly Color ColDisabled = new Color("555555");
    private static readonly Color ColBg       = new Color(0f, 0f, 0f, 0.72f);

    private bool _isClientMode; // true = client; run settings are read-only

    // Run setting checkboxes
    private CheckBox? _cbSplitMaxHp;
    private CheckBox? _cbSplitHeal;
    private CheckBox? _cbShareGold;
    private CheckBox? _cbSplitGold;

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

            // Anchor to bottom-left corner.
            AnchorLeft = AnchorRight = 0f;
            AnchorTop = AnchorBottom = 1f;
            OffsetLeft   = LeftMargin;
            OffsetRight  = LeftMargin + PanelW;
            OffsetBottom = -BottomMargin;
            OffsetTop    = -BottomMargin - EstimatedHeight();

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
    }

    /// <summary>
    /// Updates all checkbox states to match current settings and locks/unlocks controls.
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
        var header = MakeLabel("⊞  SOUL LINK SETTINGS", ColHeader, 15);
        vbox.AddChild(header);
        vbox.AddChild(MakeSeparator());

        // ── Run Settings ──────────────────────────────────────────────────────
        vbox.AddChild(MakeLabel("  RUN SETTINGS  (host only)", ColSection, 12));

        _cbSplitMaxHp = MakeCheckBox("Split MaxHP changes by players");
        _cbSplitMaxHp.Toggled += on => { SoulLinkSettings.Instance.SplitMaxHp = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbSplitMaxHp);

        _cbSplitHeal = MakeCheckBox("Split healing by players");
        _cbSplitHeal.Toggled += on => { SoulLinkSettings.Instance.SplitHeal = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbSplitHeal);

        _cbShareGold = MakeCheckBox("Share gold pool");
        _cbShareGold.Toggled += on =>
        {
            SoulLinkSettings.Instance.ShareGold = on;
            SoulLinkSettings.Save();
            // SplitGold is only meaningful when gold is shared.
            if (_cbSplitGold != null)
                _cbSplitGold.Disabled = !(on && !_isClientMode && !SoulLinkSession.IsActive);
        };
        vbox.AddChild(_cbShareGold);

        _cbSplitGold = MakeCheckBox("Split gold changes by players");
        _cbSplitGold.Toggled += on => { SoulLinkSettings.Instance.SplitGold = on; SoulLinkSettings.Save(); };
        vbox.AddChild(_cbSplitGold);

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
        OffsetTop = OffsetBottom - EstimatedHeight();
    }

    // ── Refresh logic ─────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        bool runActive   = SoulLinkSession.IsActive;
        bool runEditable = !_isClientMode && !runActive;

        // Decide which values to display:
        // - Client during active run: show locked ActiveRunSettings from host.
        // - Everyone else: show current SoulLinkSettings.
        bool splitMaxHp, splitHeal, shareGold, splitGold;
        if (_isClientMode && runActive)
        {
            var rs = SoulLinkSession.ActiveRunSettings;
            splitMaxHp = rs.SplitMaxHp;
            splitHeal  = rs.SplitHeal;
            shareGold  = rs.ShareGold;
            splitGold  = rs.SplitGold;
        }
        else
        {
            var s = SoulLinkSettings.Instance;
            splitMaxHp = s.SplitMaxHp;
            splitHeal  = s.SplitHeal;
            shareGold  = s.ShareGold;
            splitGold  = s.SplitGold;
        }

        SetCheckBox(_cbSplitMaxHp, splitMaxHp, runEditable);
        SetCheckBox(_cbSplitHeal,  splitHeal,  runEditable);
        SetCheckBox(_cbShareGold,  shareGold,  runEditable);
        // SplitGold only editable when ShareGold is on (and run is editable).
        SetCheckBox(_cbSplitGold, splitGold, runEditable && shareGold);

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
    /// Rough height estimate based on fixed item count so we can set OffsetTop
    /// before the panel is added to the layout tree (no measure pass available yet).
    /// </summary>
    private static float EstimatedHeight()
    {
        // 1 header + 1 sep + 1 section label + 4 checkboxes + 1 sep + 1 section label + 3 checkboxes
        // ≈ 12 items × ~24px + 16px margins
        return 12 * 24f + 16f;
    }
}
