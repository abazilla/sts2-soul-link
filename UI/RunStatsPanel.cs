using System;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SoulLinkMod.UI;

/// <summary>
/// Collapsible run-stats panel shown outside of combat (map, rest, shop, events).
/// Displays cumulative damage taken, healing gained, gold earned, and gold spent
/// for the entire Soul Link run so far.
///
/// Added as a child of the non-combat room node by RoomReadyPatch.
/// Hides itself when combat is in progress.
/// </summary>
public class RunStatsPanel : Control
{
    public static RunStatsPanel? Current;

    private bool _expanded = true;
    private Button? _toggleButton;
    private VBoxContainer? _statsContainer;
    private Label? _damageLbl;
    private Label? _healLbl;
    private Label? _goldEarnedLbl;
    private Label? _goldSpentLbl;

    private static readonly Color ColorDamage   = new(1f,   0.2f, 0.2f, 1f);
    private static readonly Color ColorHeal     = new(0.2f, 1f,   0.3f, 1f);
    private static readonly Color ColorGoldGain = new(1f,   0.9f, 0f,   1f);
    private static readonly Color ColorGoldSpend= new(1f,   0.65f,0f,   1f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        try
        {
            Current = this;

            // Anchor to top-left.
            AnchorLeft = 0f; AnchorRight  = 0f;
            AnchorTop  = 0f; AnchorBottom = 0f;
            OffsetLeft = 10; OffsetTop    = 10;
            OffsetRight= 220; OffsetBottom = 150;

            MouseFilter = MouseFilterEnum.Pass;

            BuildUI();
            Refresh();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] RunStatsPanel.Initialize crashed: {ex}");
        }
    }

    public void Refresh()
    {
        try   { DoRefresh(); }
        catch (Exception ex) { GD.PrintErr($"[SoulLink] RunStatsPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var bg = new PanelContainer();
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var vbox = new VBoxContainer();
        bg.AddChild(vbox);

        _toggleButton = new Button { Text = "Run Stats −" };
        _toggleButton.Pressed += OnToggle;
        vbox.AddChild(_toggleButton);

        _statsContainer = new VBoxContainer();
        vbox.AddChild(_statsContainer);

        _damageLbl    = MakeStatLabel();
        _healLbl      = MakeStatLabel();
        _goldEarnedLbl= MakeStatLabel();
        _goldSpentLbl = MakeStatLabel();

        _statsContainer.AddChild(_damageLbl);
        _statsContainer.AddChild(_healLbl);
        _statsContainer.AddChild(_goldEarnedLbl);
        _statsContainer.AddChild(_goldSpentLbl);
    }

    private static Label MakeStatLabel()
    {
        var lbl = new Label();
        lbl.HorizontalAlignment = HorizontalAlignment.Left;
        return lbl;
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        // Hide during combat — the combat log panel takes over.
        bool inCombat = CombatManager.Instance.IsInProgress;
        Visible = SoulLinkSession.IsActive && !inCombat;
        if (!Visible) return;

        if (_damageLbl    != null) SetStat(_damageLbl,     "Damage taken",  SoulLinkSession.TotalDamageTaken,   ColorDamage);
        if (_healLbl      != null) SetStat(_healLbl,       "Healing gained",SoulLinkSession.TotalHealingGained, ColorHeal);
        if (_goldEarnedLbl!= null) SetStat(_goldEarnedLbl, "Gold earned",   SoulLinkSession.TotalGoldEarned,    ColorGoldGain);
        if (_goldSpentLbl != null) SetStat(_goldSpentLbl,  "Gold spent",    SoulLinkSession.TotalGoldSpent,     ColorGoldSpend);

        if (_statsContainer != null)
            _statsContainer.Visible = _expanded;

        if (_toggleButton != null)
            _toggleButton.Text = _expanded ? "Run Stats −" : "Run Stats +";
    }

    private static void SetStat(Label lbl, string label, int value, Color color)
    {
        lbl.Text              = $"{label}:  {value}";
        lbl.AddThemeColorOverride("font_color", color);
    }

    private void OnToggle()
    {
        _expanded = !_expanded;
        Refresh();
    }
}
