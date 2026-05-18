using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

using SoulLinkMod;
using SoulLinkMod.Localization;

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

    private const string HexDamage    = "ff3333";
    private const string HexHeal      = "33ff4d";
    private const string HexGoldGain  = "ffe519";
    private const string HexGoldSpend = "ffa600";
    private const string HexBlocked   = "808080";
    private const string HexSource    = "ffffff";
    private const string HexHeader    = "999999";
    private static readonly Color ColorAccentYellow = new Color("efc851");

    private RichTextLabel? _label;
    private MegaCrit.Sts2.Core.Localization.LocManager.LocaleChangeCallback? _localeChangeCb;

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
            _toggleButton = new Button { Text = Loc.T("feed.header_expanded") };
            _toggleButton.Pressed += OnToggle;
            ApplyHeaderButtonStyle(_toggleButton);
            ApplyHeaderTextStyle(_toggleButton);
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
            _label.AddThemeConstantOverride("shadow_offset_x", 1);
            _label.AddThemeConstantOverride("shadow_offset_y", 1);
            _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.85f));
            SoulLinkFont.Apply(_label);
            vbox.AddChild(_label);

            SoulLinkLog.Debug($"CombatLogPanel initialized at OffsetLeft={OffsetLeft} OffsetTop={OffsetTop}");
            Refresh();

            // DoRefresh rebuilds the toggle label + body from Loc.T, so a plain
            // Refresh() picks up locale changes — no subtree rebuild needed.
            _localeChangeCb = Refresh;
            LocaleBus.Subscribe(_localeChangeCb);
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"CombatLogPanel.Initialize crashed: {ex}");
        }
    }

    public override void _ExitTree()
    {
        if (_localeChangeCb != null)
        {
            LocaleBus.Unsubscribe(_localeChangeCb);
            _localeChangeCb = null;
        }
        if (Current == this) Current = null;
    }

    public void Refresh()
    {
        if (_label == null) return;
        try   { DoRefresh(); }
        catch (Exception ex) { SoulLinkLog.Error($"CombatLogPanel.Refresh crashed: {ex}"); }
    }

    public static void Clear() => Current = null;

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void DoRefresh()
    {
        if (_label == null || _toggleButton == null) return;

        bool visible = SoulLinkSession.IsActive;
        Visible = visible;
        if (!visible) return;

        _toggleButton.Text = Loc.T(_expanded ? "feed.header_expanded" : "feed.header_collapsed");
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
            sb.Append($"[right][color=#{HexBlocked}]{Loc.T("feed.no_events")}[/color][/right]\n");
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

    // ── Formatting ────────────────────────────────────────────────────────────

    private static List<(string text, string hex)> FormatEntry(LogEntry entry, RunState runState)
    {
        int slot = entry.PlayerSlot;
        Player? player = (slot >= 0 && slot < runState.Players.Count) ? runState.Players[slot] : null;
        string playerHex  = GetPlayerHex(slot, player);
        string playerName = GetPlayerName(slot, runState);

        return entry.Type == LogEntryType.Health
            ? FormatHealth(playerName, playerHex, entry)
            : FormatGold(playerName, playerHex, entry);
    }

    // Per-player colour = shade of class MapDrawingColor, varied by slot so two
    // players sharing a class are still distinguishable.
    private static string GetPlayerHex(int slot, Player? player)
    {
        if (player == null) return "ffffff";
        Color baseColor = player.Character.MapDrawingColor;
        Color shaded = slot switch
        {
            0 => baseColor.Lightened(0.35f),
            1 => baseColor.Lightened(0.55f),
            2 => baseColor.Lightened(0.15f),
            _ => baseColor.Lightened(0.7f),
        };
        return shaded.ToHtml(false);
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

    private static readonly Dictionary<ulong, string> _nameCache = new();
    private static readonly HashSet<ulong> _diagLogged = new();

    private static string GetPlayerName(int slot, RunState runState)
    {
        if (slot < 0 || slot >= runState.Players.Count)
            return $"P{slot}";

        var player = runState.Players[slot];
        ulong netId = player.NetId;

        if (_nameCache.TryGetValue(netId, out var cached))
            return cached;

        string resolved = ResolveNameUncached(player, netId, slot);
        if (!string.IsNullOrEmpty(resolved) && netId != 0)
            _nameCache[netId] = resolved;
        return resolved;
    }

    private static string ResolveNameUncached(Player player, ulong netId, int slot)
    {
        string fallback = player.Character.GetType().Name;
        try
        {
            var platform = RunManager.Instance?.NetService?.Platform ?? PlatformUtil.PrimaryPlatform;
            string resolved = PlatformUtil.GetPlayerName(platform, netId);

            // SteamPlatformUtilStrategy returns netId.ToString() when Steam lookup fails;
            // treat that bare-number result as unresolved.
            bool looksLikeRawId = ulong.TryParse(resolved, out _);

            if (_diagLogged.Add(netId))
            {
                SoulLinkLog.Debug($"GetPlayerName slot={slot} netId={netId} " +
                         $"platform={platform} resolved='{resolved}' fallback='{fallback}'");
            }

            return (string.IsNullOrEmpty(resolved) || looksLikeRawId) ? fallback : resolved;
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"GetPlayerName slot={slot} netId={netId} crashed: {ex.Message}");
            return fallback;
        }
    }

    /// <summary>Escapes characters that BBCode would misinterpret (square brackets).</summary>
    private static string SecurityElement_Escape(string text) =>
        text.Replace("[", "[[");
}
