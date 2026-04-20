using System;
using Godot;
using MegaCrit.Sts2.Core.Runs;

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
/// Cumulative run totals (damage, healing, gold) are intentionally omitted here;
/// they are already shown by RunStatsPanel.
///
/// Visible whenever a Soul Link session is active (combat and non-combat).
/// Collapsible and draggable. Added as a child of every room node by RoomReadyPatch.
/// Polls via _Process so it stays current without sync-patch hooks.
/// </summary>
public class DebugOverlay : Control
{
    public static DebugOverlay? Current;

    private const int MaxPlayers = 4;
    private const float PanelW   = 220f;
    private const float PanelH   = 140f;

    // Persists position across room transitions.
    private static float _savedLeft = 10f;
    private static float _savedTop  = 90f;

    private bool _expanded = true;
    private Button? _toggleButton;
    private VBoxContainer? _content;
    private Label? _versionLbl;
    private Label? _sessionHpLbl;
    private Label? _sessionGoldLbl;
    private readonly Label[] _playerLbls = new Label[MaxPlayers];

    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _posStart;

    private static readonly Color ColorHp      = new(0.6f, 1f,   0.6f, 1f);
    private static readonly Color ColorGold    = new(1f,   0.9f, 0f,   1f);
    private static readonly Color ColorPlayer  = new(0.8f, 0.8f, 1f,   1f);
    private static readonly Color ColorMismatch= new(1f,   0.4f, 0.1f, 1f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        try
        {
            Current = this;

            // Anchor to middle-left, below RunStatsPanel (which occupies -80..+80).
            AnchorLeft = 0f; AnchorRight  = 0f;
            AnchorTop  = 0.5f; AnchorBottom = 0.5f;
            OffsetLeft   = _savedLeft;
            OffsetTop    = _savedTop;
            OffsetRight  = _savedLeft + PanelW;
            OffsetBottom = _savedTop  + PanelH;

            MouseFilter = MouseFilterEnum.Pass;
            SetProcess(true);

            BuildUI();
            DoRefresh();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] DebugOverlay.Initialize crashed: {ex}");
        }
    }

    public static void Clear() => Current = null;

    public void Refresh()
    {
        try { DoRefresh(); }
        catch (Exception ex) { GD.PrintErr($"[SoulLink] DebugOverlay.Refresh crashed: {ex}"); }
    }

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

        _toggleButton = new Button { Text = "Soul Link Debug −" };
        _toggleButton.Pressed += OnToggle;
        vbox.AddChild(_toggleButton);

        _content = new VBoxContainer();
        _content.MouseFilter = MouseFilterEnum.Pass;
        vbox.AddChild(_content);

        _versionLbl     = MakeLabel();
        _sessionHpLbl   = MakeLabel();
        _sessionGoldLbl = MakeLabel();
        _content.AddChild(_versionLbl);
        _content.AddChild(_sessionHpLbl);
        _content.AddChild(_sessionGoldLbl);

        _content.AddChild(new HSeparator());

        var playerHeader = MakeLabel();
        playerHeader.Text = "Game HP (actual):";
        _content.AddChild(playerHeader);

        for (int i = 0; i < MaxPlayers; i++)
        {
            _playerLbls[i] = MakeLabel();
            _content.AddChild(_playerLbls[i]);
        }
    }

    private static Label MakeLabel()
        => new() { HorizontalAlignment = HorizontalAlignment.Left };

    // ── Refresh ───────────────────────────────────────────────────────────────

    public override void _Process(double _delta)
    {
        try { DoRefresh(); }
        catch (Exception ex) { GD.PrintErr($"[SoulLink] DebugOverlay._Process crashed: {ex}"); }
        try { HandleDrag(); }
        catch { /* don't let drag errors crash the overlay */ }
    }

    private void HandleDrag()
    {
        var viewport = GetViewport();
        if (viewport == null) return;

        var mousePos   = viewport.GetMousePosition();
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

    private void DoRefresh()
    {
        Visible = SoulLinkSession.IsActive;
        if (!Visible) return;

        Set(_versionLbl,     BuildInfo.Version,                                                        new Color(0.6f, 0.6f, 0.6f, 1f));
        Set(_sessionHpLbl,   $"Session HP:  {SoulLinkSession.CurrentHp} / {SoulLinkSession.MaxHp}", ColorHp);
        Set(_sessionGoldLbl, $"Session Gold:  {SoulLinkSession.Gold}",                               ColorGold);

        var runState = RunManager.Instance?.DebugOnlyGetState();
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_playerLbls[i] == null) continue;

            if (runState != null && i < runState.Players.Count)
            {
                var p        = runState.Players[i];
                int actual   = p.Creature.CurrentHp;
                int max      = p.Creature.MaxHp;
                string name  = p.Character.GetType().Name;
                bool mismatch= actual != SoulLinkSession.CurrentHp;
                _playerLbls[i].Text    = $"P{i} {name}: {actual}/{max}";
                _playerLbls[i].AddThemeColorOverride("font_color", mismatch ? ColorMismatch : ColorPlayer);
                _playerLbls[i].Visible = true;
            }
            else
            {
                _playerLbls[i].Visible = false;
            }
        }

        if (_content != null)
            _content.Visible = _expanded;

        if (_toggleButton != null)
            _toggleButton.Text = _expanded ? "Soul Link Debug −" : "Soul Link Debug +";
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
