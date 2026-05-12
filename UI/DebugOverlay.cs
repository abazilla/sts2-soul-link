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
/// Visible whenever a Soul Link session is active (combat and non-combat).
/// Collapsible. Polls via _Process so it stays current without sync-patch hooks.
/// When collapsed, the content background is hidden; only the header button remains.
/// </summary>
public class DebugOverlay : Control
{
    public static DebugOverlay? Current;

    private const int MaxPlayers = 4;
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
    private Label? _settingsLbl1;
    private Label? _settingsLbl2;
    private readonly Label[] _playerLbls = new Label[MaxPlayers];

    private static readonly Color ColorHp       = new(0.6f, 1f,   0.6f, 1f);
    private static readonly Color ColorGold     = new(1f,   0.9f, 0f,   1f);
    private static readonly Color ColorPlayer   = new(0.8f, 0.8f, 1f,   1f);
    private static readonly Color ColorMismatch = new(1f,   0.4f, 0.1f, 1f);

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
        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(vbox);

        // Header button — borderless, looks like a plain clickable label.
        _toggleButton = new Button { Text = $"Soul Link Debug ({SoulLinkMod.Version}) v" };
        _toggleButton.Pressed += OnToggle;
        ApplyHeaderButtonStyle(_toggleButton);
        SoulLinkFont.Apply(_toggleButton);
        vbox.AddChild(_toggleButton);

        // Content background — hidden when collapsed.
        _bgPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddChild(_bgPanel);

        var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _bgPanel.AddChild(content);

        _sessionHpLbl   = MakeLabel();
        _sessionGoldLbl = MakeLabel();
        content.AddChild(_sessionHpLbl);
        content.AddChild(_sessionGoldLbl);

        content.AddChild(new HSeparator());

        _settingsLbl1 = MakeLabel();
        _settingsLbl2 = MakeLabel();
        content.AddChild(_settingsLbl1);
        content.AddChild(_settingsLbl2);

        content.AddChild(new HSeparator());

        var playerHeader = MakeLabel();
        playerHeader.Text = "Game HP (actual):";
        content.AddChild(playerHeader);

        for (int i = 0; i < MaxPlayers; i++)
        {
            _playerLbls[i] = MakeLabel();
            content.AddChild(_playerLbls[i]);
        }
    }

    private static Label MakeLabel()
    {
        var lbl = new Label { HorizontalAlignment = HorizontalAlignment.Left };
        SoulLinkFont.Apply(lbl);
        return lbl;
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

        Set(_sessionHpLbl, $"Session HP:  {SoulLinkSession.CurrentHp} / {SoulLinkSession.MaxHp}", ColorHp);

        string goldLine = rs.GoldMode switch
        {
            GoldSharingMode.SplitByPlayer => $"Gold P0:{SoulLinkSession.GetPlayerGold(0)}  P1:{SoulLinkSession.GetPlayerGold(1)}",
            GoldSharingMode.SharedPool    => $"Session Gold:  {SoulLinkSession.Gold}",
            _                             => "Gold: STS2 default",
        };
        Set(_sessionGoldLbl, goldLine, ColorGold);

        Set(_settingsLbl1, $"SplitHP:{rs.SplitMaxHp}  SplitHeal:{rs.SplitHeal}", ColorPlayer);
        Set(_settingsLbl2, $"GoldMode:{rs.GoldMode}", ColorPlayer);

        var runState = RunManager.Instance?.DebugOnlyGetState();
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (_playerLbls[i] == null) continue;

            if (runState != null && i < runState.Players.Count)
            {
                var p       = runState.Players[i];
                int actual  = p.Creature.CurrentHp;
                int max     = p.Creature.MaxHp;
                string name = p.Character.GetType().Name;
                bool mismatch = actual != SoulLinkSession.CurrentHp;
                _playerLbls[i].Text = $"P{i} {name}: {actual}/{max}";
                _playerLbls[i].AddThemeColorOverride("font_color", mismatch ? ColorMismatch : ColorPlayer);
                _playerLbls[i].Visible = true;
            }
            else
            {
                _playerLbls[i].Visible = false;
            }
        }

        if (_bgPanel != null)
            _bgPanel.Visible = _expanded;

        if (_toggleButton != null)
            _toggleButton.Text = $"Soul Link Debug ({SoulLinkMod.Version}) {(_expanded ? "v" : ">")}";
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
