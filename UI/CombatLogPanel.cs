using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using Steamworks;

namespace SoulLinkMod.UI;

/// <summary>
/// In-combat HUD overlay showing the last 5 HP/gold change log entries,
/// right-aligned in the top-right corner of the screen.
///
/// Added as a child of NCombatRoom by CombatRoomReadyPatch.
/// </summary>
public class CombatLogPanel : Control
{
    public static CombatLogPanel? Current;

    private const int MaxVisible  = 5;
    private const int LineHeight  = 22;
    private const int RightMargin = 10;
    private const int StartYFromBottom = 160;

    // Player slot → color
    private static readonly Color[] PlayerColors =
    {
        new(0f,   1f,   1f,   1f),   // slot 0 — cyan
        new(1f,   0.85f,0f,   1f),   // slot 1 — gold/yellow
        new(1f,   0.3f, 1f,   1f),   // slot 2 — magenta/pink
        new(0.5f, 1f,   0.5f, 1f),   // slot 3 — light green
    };

    private static readonly Color ColorDamage  = new(1f,   0.2f, 0.2f, 1f);   // red
    private static readonly Color ColorHeal    = new(0.2f, 1f,   0.3f, 1f);   // green
    private static readonly Color ColorMaxHp   = new(1f,   0.65f,0f,   1f);   // orange
    private static readonly Color ColorGoldGain = new(1f,  0.9f, 0f,   1f);   // yellow
    private static readonly Color ColorGoldSpend = new(1f, 0.65f,0f,   1f);   // orange
    private static readonly Color ColorBlocked = new(0.5f, 0.5f, 0.5f, 1f);   // gray

    private Font? _font;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        try
        {
            Current = this;

            // Fill the screen so DrawString coordinates are relative to top-left.
            AnchorLeft   = 0f; AnchorRight  = 1f;
            AnchorTop    = 0f; AnchorBottom = 1f;
            OffsetLeft   = 0;  OffsetRight  = 0;
            OffsetTop    = 0;  OffsetBottom = 0;

            MouseFilter = MouseFilterEnum.Ignore;

            // Use the default theme font; falls back to the built-in font.
            _font = ThemeDB.FallbackFont;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SoulLink] CombatLogPanel.Initialize crashed: {ex}");
        }
    }

    public void Refresh() => QueueRedraw();

    public static void Clear() => Current = null;

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (!SoulLinkSession.IsActive) return;

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        var entries = SoulLinkSession.Log.Take(MaxVisible).ToList();
        if (entries.Count == 0) return;

        float screenW = Size.X;
        float screenH = Size.Y;
        float rightEdge = screenW - RightMargin;
        float startY    = screenH - StartYFromBottom;

        Font font = _font ?? ThemeDB.FallbackFont;
        int fontSize = 14;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float y = startY - i * LineHeight;

            (string playerText, string actionText, Color playerColor, Color actionColor)
                = FormatEntry(entry, runState);

            // Measure combined width so both pieces flush against the right edge.
            float playerW = font.GetStringSize(playerText, fontSize: fontSize).X;
            float actionW = font.GetStringSize(actionText, fontSize: fontSize).X;
            float totalW  = playerW + actionW;

            float xPlayer = rightEdge - totalW;
            float xAction = xPlayer + playerW;

            DrawString(font, new Vector2(xPlayer, y), playerText, fontSize: fontSize, modulate: playerColor);
            DrawString(font, new Vector2(xAction,  y), actionText, fontSize: fontSize, modulate: actionColor);
        }
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private static (string playerText, string actionText, Color playerColor, Color actionColor)
        FormatEntry(LogEntry entry, RunState runState)
    {
        Color playerColor = PlayerColors[Math.Min(entry.PlayerSlot, PlayerColors.Length - 1)];
        string playerName = GetPlayerName(entry.PlayerSlot, runState);
        string playerText = $"[{playerName}] ";

        if (entry.Type == LogEntryType.Health)
        {
            string actionText;
            Color actionColor;

            if (entry.Delta != 0 && entry.MaxHpDelta != 0)
            {
                // Both current and max HP changed.
                if (entry.Delta > 0 && entry.MaxHpDelta > 0)
                {
                    actionText  = $"healed {entry.Delta} HP and gained {entry.MaxHpDelta} max HP";
                    actionColor = ColorHeal;
                }
                else
                {
                    actionText  = $"lost {-entry.Delta} HP and {-entry.MaxHpDelta} max HP";
                    actionColor = ColorDamage;
                }
            }
            else if (entry.Delta < 0)
            {
                string src = entry.Source != null ? $" from {entry.Source}" : "";
                actionText  = $"took {-entry.Delta} damage{src}";
                actionColor = ColorDamage;
            }
            else if (entry.Delta > 0)
            {
                string src = entry.Source != null ? $" via {entry.Source}" : "";
                actionText  = $"healed {entry.Delta} health{src}";
                actionColor = ColorHeal;
            }
            else if (entry.MaxHpDelta > 0)
            {
                string src = entry.Source != null ? $" via {entry.Source}" : "";
                actionText  = $"gained {entry.MaxHpDelta} max HP{src}";
                actionColor = ColorMaxHp;
            }
            else
            {
                string src = entry.Source != null ? $" from {entry.Source}" : "";
                actionText  = $"lost {-entry.MaxHpDelta} max HP{src}";
                actionColor = ColorMaxHp;
            }

            return (playerText, actionText, playerColor, actionColor);
        }
        else // Gold
        {
            string actionText;
            Color actionColor;

            if (entry.Blocked)
            {
                string relic = entry.Source ?? "Relic";
                actionText  = $"couldn't gain {entry.Delta} gold ({relic})";
                actionColor = ColorBlocked;
            }
            else if (entry.Delta > 0)
            {
                actionText  = $"gained {entry.Delta} gold";
                actionColor = ColorGoldGain;
            }
            else
            {
                actionText  = $"spent {-entry.Delta} gold";
                actionColor = ColorGoldSpend;
            }

            return (playerText, actionText, playerColor, actionColor);
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
}
