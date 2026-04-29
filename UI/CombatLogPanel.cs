using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using Steamworks;

namespace SoulLinkMod.UI;

/// <summary>
/// Kill-feed style HUD overlay showing the last 7 HP/gold change log entries.
/// Positioned in the top-right corner (below the menu bar).
///
/// Uses a RichTextLabel with BBCode — same rendering pattern as RunStatsPanel
/// (real Godot node with explicit size, no custom _Draw override).
///
/// Added as a child of a CanvasLayer (Layer=100) inside each room by RoomReadyPatch.
/// </summary>
public class CombatLogPanel : Control
{
    public static CombatLogPanel? Current;

    private const int MaxVisible    = 7;
    private const int LineHeight    = 22;
    private const float PanelW      = 500f;
    private const float PanelH      = (MaxVisible + 1) * LineHeight + 10f;
    private const float RightMargin = 10f;
    private const float TopMargin   = 150f;

    private bool _expanded = true;
    private Button? _toggleButton;

    // Player slot → color hex (no alpha prefix)
    private static readonly string[] PlayerColorHex =
    {
        "00ffff",   // slot 0 — cyan
        "ffd900",   // slot 1 — gold/yellow
        "ff4dff",   // slot 2 — magenta/pink
        "80ff80",   // slot 3 — light green
    };

    private const string HexDamage    = "ff3333";
    private const string HexHeal      = "33ff4d";
    private const string HexGoldGain  = "ffe519";
    private const string HexGoldSpend = "ffa600";
    private const string HexBlocked   = "808080";
    private const string HexSource    = "ffffff";
    private const string HexHeader    = "999999";

    private RichTextLabel? _label;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        try
        {
            Current = this;

            // Pin to top-right corner, same explicit-offset pattern as RunStatsPanel.
            AnchorLeft   = 1f; AnchorRight  = 1f;
            AnchorTop    = 0f; AnchorBottom = 0f;
            OffsetLeft   = -(RightMargin + PanelW);
            OffsetTop    = TopMargin;
            OffsetRight  = -RightMargin;
            OffsetBottom = TopMargin + PanelH;

            MouseFilter = MouseFilterEnum.Pass;

            var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(vbox);

            // Header toggle button — mirrors RunStatsPanel's borderless button style.
            _toggleButton = new Button { Text = "Soul Link Feed v" };
            _toggleButton.Pressed += OnToggle;
            ApplyHeaderButtonStyle(_toggleButton);
            SoulLinkFont.Apply(_toggleButton);
            vbox.AddChild(_toggleButton);

            // Content label — hidden when collapsed.
            _label = new RichTextLabel
            {
                BbcodeEnabled  = true,
                AutowrapMode   = TextServer.AutowrapMode.Off,
                ScrollActive   = false,
                FitContent     = false,
                MouseFilter    = MouseFilterEnum.Ignore,
            };
            _label.CustomMinimumSize = new Vector2(PanelW, MaxVisible * LineHeight);
            _label.AddThemeFontSizeOverride("normal_font_size", 15);
            SoulLinkFont.Apply(_label);
            vbox.AddChild(_label);

            GD.Print($"[SoulLink] CombatLogPanel initialized at OffsetLeft={OffsetLeft} OffsetTop={OffsetTop}");
            Refresh();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] CombatLogPanel.Initialize crashed: {ex}");
        }
    }

    public void Refresh()
    {
        if (_label == null) return;
        try   { DoRefresh(); }
        catch (Exception ex) { GD.PrintErr($"[SoulLink] CombatLogPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        if (_label == null || _toggleButton == null) return;

        bool visible = SoulLinkSession.IsActive;
        Visible = visible;
        if (!visible) return;

        _toggleButton.Text = _expanded ? "Soul Link Feed v" : "Soul Link Feed >";
        _label.Visible = _expanded;

        if (!_expanded) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        var sb = new StringBuilder();

        if (runState == null)
        {
            _label.Text = sb.ToString();
            return;
        }

        var entries = SoulLinkSession.Log.Take(MaxVisible).ToList();
        if (entries.Count == 0)
        {
            sb.Append($"[right][color=#{HexBlocked}](no events yet)[/color][/right]\n");
            _label.Text = sb.ToString();
            return;
        }

        foreach (var entry in entries)
        {
            sb.Append("[right]");
            foreach (var (text, hex) in FormatEntry(entry, runState))
                sb.Append($"[color=#{hex}]{SecurityElement_Escape(text)}[/color]");
            sb.Append("[/right]\n");
        }

        _label.Text = sb.ToString();
    }

    private void OnToggle()
    {
        _expanded = !_expanded;
        Refresh();
    }

    private static void ApplyHeaderButtonStyle(Button btn)
    {
        btn.Alignment = HorizontalAlignment.Right;
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal",   empty);
        btn.AddThemeStyleboxOverride("hover",    empty);
        btn.AddThemeStyleboxOverride("pressed",  empty);
        btn.AddThemeStyleboxOverride("focus",    empty);
        btn.AddThemeStyleboxOverride("disabled", empty);
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private static List<(string text, string hex)> FormatEntry(LogEntry entry, RunState runState)
    {
        string playerHex  = PlayerColorHex[Math.Min(entry.PlayerSlot, PlayerColorHex.Length - 1)];
        string playerName = GetPlayerName(entry.PlayerSlot, runState);

        return entry.Type == LogEntryType.Health
            ? FormatHealth(playerName, playerHex, entry)
            : FormatGold(playerName, playerHex, entry);
    }

    private static List<(string, string)> FormatHealth(string name, string nameHex, LogEntry e)
    {
        var segs = new List<(string, string)> { ($"{name} ", nameHex) };

        int d = e.Delta, md = e.MaxHpDelta;

        if (d < 0)
        {
            segs.Add(($"took {-d} damage", HexDamage));
            AppendSource(segs, "from", e.Source);
        }
        else if (d > 0 && md > 0)
        {
            segs.Add(($"healed {d} HP and gained {md} max HP", HexHeal));
            AppendSource(segs, "via", e.Source);
        }
        else if (d > 0)
        {
            segs.Add(($"healed {d} health", HexHeal));
            AppendSource(segs, "via", e.Source);
        }
        else if (md > 0)
        {
            segs.Add(($"gained {md} max HP", HexHeal));
            AppendSource(segs, "via", e.Source);
        }
        else if (md < 0)
        {
            segs.Add(($"lost {-md} max HP", HexDamage));
            AppendSource(segs, "via", e.Source);
        }

        return segs;
    }

    private static List<(string, string)> FormatGold(string name, string nameHex, LogEntry e)
    {
        var segs = new List<(string, string)> { ($"{name} ", nameHex) };

        if (e.Blocked)
        {
            segs.Add(($"couldn't gain {e.Delta} gold ({e.Source ?? "Relic"})", HexBlocked));
        }
        else if (e.Delta > 0)
        {
            segs.Add(($"gained {e.Delta} gold", HexGoldGain));
            AppendSource(segs, "from", e.Source);
        }
        else
        {
            segs.Add(($"spent {-e.Delta} gold", HexGoldSpend));
            AppendSource(segs, "via", e.Source);
        }

        return segs;
    }

    private static void AppendSource(List<(string, string)> segs, string prep, string? source)
    {
        if (!string.IsNullOrEmpty(source))
        {
            segs.Add(($" {prep} ", HexSource));
            segs.Add((source, HexSource));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetPlayerName(int slot, RunState runState)
    {
        if (slot < 0 || slot >= runState.Players.Count)
            return $"P{slot}";

        var player = runState.Players[slot];
        try
        {
            ulong localId = SteamUser.GetSteamID().m_SteamID;
            string name = player.NetId == localId
                ? SteamFriends.GetPersonaName()
                : SteamFriends.GetFriendPersonaName(new CSteamID(player.NetId));
            return string.IsNullOrEmpty(name) ? player.Character.GetType().Name : name;
        }
        catch
        {
            return player.Character.GetType().Name;
        }
    }

    /// <summary>Escapes characters that BBCode would misinterpret (square brackets).</summary>
    private static string SecurityElement_Escape(string text) =>
        text.Replace("[", "[[");
}
