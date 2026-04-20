using System;
using Godot;

namespace SoulLinkMod.UI;

/// <summary>
/// Collapsible run-stats panel showing cumulative damage taken, healing gained,
/// gold earned, and gold spent for the entire Soul Link run.
///
/// Added as a child of both combat and non-combat room nodes by RoomReadyPatch.
/// Panel position persists across room transitions via static saved offsets.
/// </summary>
public class RunStatsPanel : Control
{
    public static RunStatsPanel? Current;

    private const float PanelW = 210f;
    private const float PanelH = 160f;

    // Persists position across room transitions (static survives node destruction).
    private static float _savedLeft = 10f;
    private static float _savedTop  = -80f;

    private bool _expanded = true;
    private Button? _toggleButton;
    private VBoxContainer? _statsContainer;
    private Label? _damageLbl;
    private Label? _healLbl;
    private Label? _goldEarnedLbl;
    private Label? _goldSpentLbl;

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _posStart;

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

            AnchorLeft = 0f; AnchorRight  = 0f;
            AnchorTop  = 0.5f; AnchorBottom = 0.5f;
            OffsetLeft   = _savedLeft;
            OffsetTop    = _savedTop;
            OffsetRight  = _savedLeft + PanelW;
            OffsetBottom = _savedTop  + PanelH;

            MouseFilter = MouseFilterEnum.Pass;
            SetProcess(true);

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
        bg.MouseFilter = MouseFilterEnum.Pass;
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.MouseFilter = MouseFilterEnum.Pass;
        bg.AddChild(vbox);

        _toggleButton = new Button { Text = "Run Stats −" };
        _toggleButton.Pressed += OnToggle;
        vbox.AddChild(_toggleButton);

        _statsContainer = new VBoxContainer();
        _statsContainer.MouseFilter = MouseFilterEnum.Pass;
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
        => new() { HorizontalAlignment = HorizontalAlignment.Left };

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        Visible = SoulLinkSession.IsActive;
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
        lbl.Text = $"{label}:  {value}";
        lbl.AddThemeColorOverride("font_color", color);
    }

    private void OnToggle()
    {
        _expanded = !_expanded;
        Refresh();
    }

    // ── Drag (polled via _Process) ────────────────────────────────────────────
    // _Input and _GuiInput don't fire reliably when the game captures input first.
    // Polling Input.IsMouseButtonPressed in _Process is more robust.

    public override void _Process(double _delta)
    {
        try { HandleDrag(); }
        catch { /* don't let drag errors crash the panel */ }
    }

    private void HandleDrag()
    {
        var viewport = GetViewport();
        if (viewport == null) return;

        var mousePos  = viewport.GetMousePosition();
        bool mouseDown = Input.IsMouseButtonPressed(MouseButton.Left);

        if (!_dragging && mouseDown)
        {
            bool inPanel  = GetGlobalRect().HasPoint(mousePos);
            bool onButton = _toggleButton != null && _toggleButton.GetGlobalRect().HasPoint(mousePos);
            if (inPanel && !onButton)
            {
                _dragging  = true;
                _dragStart = mousePos;
                _posStart  = new Vector2(OffsetLeft, OffsetTop);
            }
        }
        else if (_dragging && !mouseDown)
        {
            _dragging  = false;
            _savedLeft = OffsetLeft;
            _savedTop  = OffsetTop;
        }

        if (_dragging)
        {
            var d = mousePos - _dragStart;
            OffsetLeft   = _posStart.X + d.X;
            OffsetTop    = _posStart.Y + d.Y;
            OffsetRight  = OffsetLeft + PanelW;
            OffsetBottom = OffsetTop  + PanelH;
        }
    }
}
