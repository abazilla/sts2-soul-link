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
    private const string HexEvent     = "9bd1ff";
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

    // Compose a colored amount like "9 damage" / "11 max HP" / "50 gold".
    // Unit noun is locale-specific; full phrase shares one color so the entire
    // amount (number + noun) tints together in the feed.
    private static string Amt(int n, string unitKey) => $"{n} {Loc.T($"feed.unit.{unitKey}")}";

    private static List<(string, string)> FormatHealth(string name, string nameHex, LogEntry e)
    {
        int d = e.Delta, md = e.MaxHpDelta;
        bool hasSrc = !string.IsNullOrEmpty(e.Source);
        string src = ResolveSource(e.Source);

        if (d < 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.health.damage_from") : Loc.T("feed.health.damage"),
                (name, nameHex), (Amt(-d, "damage"), HexDamage), (src, HexEvent));
        if (d > 0 && md > 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.health.heal_and_max_via") : Loc.T("feed.health.heal_and_max"),
                (name, nameHex), (Amt(d, "hp"), HexHeal), (Amt(md, "max_hp"), HexHeal), (src, HexEvent));
        if (d > 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.health.heal_via") : Loc.T("feed.health.heal"),
                (name, nameHex), (Amt(d, "health"), HexHeal), (src, HexEvent));
        if (md > 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.health.max_gain_via") : Loc.T("feed.health.max_gain"),
                (name, nameHex), (Amt(md, "max_hp"), HexHeal), (src, HexEvent));
        if (md < 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.health.max_loss_via") : Loc.T("feed.health.max_loss"),
                (name, nameHex), (Amt(-md, "max_hp"), HexDamage), (src, HexEvent));

        return new List<(string, string)> { (name, nameHex) };
    }

    private static List<(string, string)> FormatGold(string name, string nameHex, LogEntry e)
    {
        bool hasSrc = !string.IsNullOrEmpty(e.Source);
        string src = hasSrc ? ResolveSource(e.Source) : Loc.T("feed.gold.default_source");

        if (e.Blocked)
            return RenderTemplate(Loc.T("feed.gold.blocked"),
                (name, nameHex), (Amt(e.Delta, "gold"), HexBlocked), (src, HexBlocked));
        if (e.Delta > 0)
            return RenderTemplate(
                hasSrc ? Loc.T("feed.gold.gain_from") : Loc.T("feed.gold.gain"),
                (name, nameHex), (Amt(e.Delta, "gold"), HexGoldGain), (src, HexEvent));
        return RenderTemplate(
            hasSrc ? Loc.T("feed.gold.spend_via") : Loc.T("feed.gold.spend"),
            (name, nameHex), (Amt(-e.Delta, "gold"), HexGoldSpend), (src, HexEvent));
    }

    // Source strings may be raw text (attacker name, free string) or a marker:
    //   "@source:<key>"   → resolved via Loc.T("feed.source.<key>")
    //   "@event:<ID>"     → resolved via vanilla "events" loc table at "<ID>.title"
    // Markers defer locale resolution until render so each viewer sees event/room
    // names in their own language.
    private static string ResolveSource(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.StartsWith("@source:", StringComparison.Ordinal))
        {
            string key = "feed.source." + raw.Substring("@source:".Length);
            string t = Loc.T(key);
            return string.IsNullOrEmpty(t) || t == key ? raw : t;
        }
        if (raw.StartsWith("@event:", StringComparison.Ordinal))
            return LookupVanilla("events", raw.Substring("@event:".Length) + ".title", raw.Substring("@event:".Length));
        if (raw.StartsWith("@relic:", StringComparison.Ordinal))
            return LookupVanilla("relics", raw.Substring("@relic:".Length) + ".title", raw.Substring("@relic:".Length));
        return raw;
    }

    private static string LookupVanilla(string table, string key, string fallbackEntry)
    {
        try
        {
            var ls = new MegaCrit.Sts2.Core.Localization.LocString(table, key);
            string s = ls.GetFormattedText();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        catch { }
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(fallbackEntry.Replace("_", " ").ToLower());
    }

    // Expand "{0} took {1} damage from {2}" into colored segments.
    // Plain text between placeholders gets the body/source neutral color.
    private static List<(string, string)> RenderTemplate(string template, params (string text, string hex)[] tokens)
    {
        var segs = new List<(string, string)>();
        int i = 0, n = template.Length;
        var lit = new StringBuilder();
        while (i < n)
        {
            char c = template[i];
            if (c == '{' && i + 1 < n && char.IsDigit(template[i + 1]))
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i && int.TryParse(template.Substring(i + 1, end - i - 1), out int idx) && idx >= 0 && idx < tokens.Length)
                {
                    if (lit.Length > 0) { segs.Add((lit.ToString(), HexSource)); lit.Clear(); }
                    segs.Add(tokens[idx]);
                    i = end + 1;
                    continue;
                }
            }
            lit.Append(c);
            i++;
        }
        if (lit.Length > 0) segs.Add((lit.ToString(), HexSource));
        return segs;
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
