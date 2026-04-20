using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod;

/// <summary>
/// Holds the canonical shared state for a Soul Link session:
/// one HP pool and one gold pool shared across all players.
///
/// IsActive must be true before any patch logic fires.
/// It is set to true by RunStartPatch once a multiplayer run is fully initialized,
/// and cleared by RunStartPatch when the run ends.
/// </summary>
public static class SoulLinkSession
{
    // ── Session gate ──────────────────────────────────────────────────────────

    /// <summary>True when a soul-link multiplayer run is in progress.</summary>
    public static bool IsActive { get; private set; }

    // ── Canonical shared pool ─────────────────────────────────────────────────

    public static int CurrentHp  { get; private set; }
    public static int MaxHp      { get; private set; }
    public static int Gold       { get; private set; }

    // ── Change log ────────────────────────────────────────────────────────────

    private const int LogCapacity = 10;
    private static readonly LinkedList<LogEntry> _log = new();
    public static IEnumerable<LogEntry> Log => _log;

    // Cumulative run totals (derived from the log as entries are added)
    public static int TotalDamageTaken  { get; private set; }
    public static int TotalHealingGained { get; private set; }
    public static int TotalGoldEarned   { get; private set; }
    public static int TotalGoldSpent    { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by RunStartPatch after the run is fully initialized.
    /// Calculates shared starting values from all players and applies them.
    /// Only activates when there is more than one player (multiplayer run).
    /// </summary>
    public static void OnRunStart()
    {
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) return;

        // Soul Link only activates for multiplayer runs.
        if (runState.Players.Count <= 1) return;

        // Debug: log each player's raw values so we can verify what the game reports at Launch() time.
        foreach (var p in runState.Players)
            GD.Print($"[SoulLink] Player {p.Character.GetType().Name}: Character.StartingHp={p.Character.StartingHp}, Creature.MaxHp={p.Creature.MaxHp}, Creature.CurrentHp={p.Creature.CurrentHp}");

        // On a fresh run, Creature.MaxHp may be 0 on the host at Launch() time — fall back
        // to Character.StartingHp (the static data value) in that case.
        // On a save-load, Creature.MaxHp holds the saved value with accumulated in-run gains.
        static int BestMaxHp(Player p) =>
            p.Creature.MaxHp > 0 ? p.Creature.MaxHp : p.Character.StartingHp;

        // Detect save-load: on a fresh run Creature.CurrentHp is 0 for all players (not yet set).
        // On a save-load the game restores it from disk so at least one player has CurrentHp > 0.
        // A run cannot be saved at 0 HP (game over), so this check is safe.
        bool isSaveLoad = false;
        foreach (var p in runState.Players)
        {
            if (p.Creature.CurrentHp > 0) { isSaveLoad = true; break; }
        }

        int sharedMaxHp = (int)Math.Floor(Average(runState.Players, p => BestMaxHp(p)));

        int sharedCurrentHp;
        if (isSaveLoad)
        {
            // Restore CurrentHp from save. All players should have the same HP due to soul link,
            // but average in case of any transient divergence. Fall back to MaxHp if 0.
            sharedCurrentHp = (int)Math.Floor(Average(runState.Players,
                p => p.Creature.CurrentHp > 0 ? p.Creature.CurrentHp : BestMaxHp(p)));
            GD.Print($"[SoulLink] Save-load detected. Restoring MaxHp={sharedMaxHp}, CurrentHp={sharedCurrentHp}");
        }
        else
        {
            // A2: Ancients (including Neow) only heal 80% of missing HP, so the effective
            // starting CurrentHp is floor(MaxHp * 0.8) — matches all characters except Defect
            // (formula gives 60, but Defect A2 is 56 — verify if this matters in practice).
            bool isA2 = runState.AscensionLevel >= 2;
            sharedCurrentHp = isA2
                ? (int)Math.Floor(sharedMaxHp * 0.8)
                : sharedMaxHp;
        }

        int sharedGold = runState.Players[0].Gold;  // all players start with the same gold

        MaxHp     = sharedMaxHp;
        CurrentHp = sharedCurrentHp;
        Gold      = sharedGold;

        // On a save-load keep cumulative totals — the run is continuing, not starting fresh.
        if (!isSaveLoad)
        {
            _log.Clear();
            TotalDamageTaken   = 0;
            TotalHealingGained = 0;
            TotalGoldEarned    = 0;
            TotalGoldSpent     = 0;
        }

        // Write canonical values before activating so no patch intercepts the initial write.
        ApplyToAllPlayers(runState);
        IsActive = true;
    }

    /// <summary>Called when the run ends (win or loss). Clears all state.</summary>
    public static void OnRunEnd()
    {
        IsActive  = false;
        CurrentHp = 0;
        MaxHp     = 0;
        Gold      = 0;
        _log.Clear();
        TotalDamageTaken   = 0;
        TotalHealingGained = 0;
        TotalGoldEarned    = 0;
        TotalGoldSpent     = 0;
    }

    // ── Apply HP delta ────────────────────────────────────────────────────────

    /// <summary>
    /// Called from HpSyncPatch when any player's CurrentHp is being written.
    /// Updates the canonical pool, logs the entry, and writes back to all players.
    /// Returns the value that should be written to the triggering player's HP
    /// (the canonical value, possibly scaled for out-of-combat heals).
    /// </summary>
    public static int ApplyHpDelta(int rawDelta, bool inCombat, int playerCount, int playerSlot, string? source = null)
    {
        int delta = rawDelta;

        // Scale out-of-combat heals by 1/playerCount so rest/events don't
        // become N× more powerful in multiplayer.
        if (delta > 0 && !inCombat && playerCount > 1)
            delta = Math.Max(1, delta / playerCount);

        CurrentHp = Math.Clamp(CurrentHp + delta, 0, MaxHp);

        AddEntry(new LogEntry(LogEntryType.Health, playerSlot, delta, 0, source));
        return CurrentHp;
    }

    /// <summary>
    /// Called from MaxHpSyncPatch. Updates canonical max HP (and clamps current HP).
    /// </summary>
    public static void ApplyMaxHpDelta(int delta, bool inCombat, int playerCount, int playerSlot, string? source = null)
    {
        int scaledDelta = delta;

        // Scale out-of-combat max HP gains the same way as regular heals.
        if (delta > 0 && !inCombat && playerCount > 1)
            scaledDelta = Math.Max(1, delta / playerCount);

        MaxHp     = Math.Max(1, MaxHp + scaledDelta);
        CurrentHp = Math.Min(CurrentHp, MaxHp);

        AddEntry(new LogEntry(LogEntryType.Health, playerSlot, 0, scaledDelta, source));
    }

    // ── Apply Gold delta ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets canonical gold directly to the given value without computing a delta or logging.
    /// Called by the SoulLinkGoldSyncMessage handler on the receiving peer so it mirrors
    /// the sender's canonical value without double-counting.
    /// </summary>
    public static void SetGoldDirect(int canonical)
    {
        Gold = Math.Max(0, canonical);
    }

    /// <summary>
    /// Called from GoldSyncPatch. Updates the canonical gold pool.
    /// If blocked (e.g. Ectoplasm), the delta is logged but NOT applied.
    /// Returns the canonical gold value that should actually be written.
    /// </summary>
    public static int ApplyGoldDelta(int delta, int playerSlot, bool blocked, string? blockSource = null)
    {
        if (blocked)
        {
            AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, delta, 0, blockSource, Blocked: true));
            return Gold; // no change
        }

        Gold = Math.Max(0, Gold + delta);
        AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, delta));
        return Gold;
    }

    // ── Write canonical state to all players ─────────────────────────────────

    /// <summary>
    /// Writes the current canonical HP/MaxHp/Gold values back to every player in the run.
    /// The ApplyingCanonical guard in each patch prevents re-entrancy.
    /// </summary>
    public static void ApplyToAllPlayers(RunState runState)
    {
        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var player in runState.Players)
            {
                player.Creature.SetMaxHp(MaxHp);
                player.Creature.SetCurrentHp(CurrentHp);
                player.Gold = Gold;
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static void AddEntry(LogEntry entry)
    {
        _log.AddFirst(entry);
        while (_log.Count > LogCapacity)
            _log.RemoveLast();

        // Update cumulative totals.
        if (entry.Type == LogEntryType.Health)
        {
            if (entry.Delta < 0)  TotalDamageTaken   += -entry.Delta;
            if (entry.Delta > 0)  TotalHealingGained += entry.Delta;
        }
        else
        {
            if (!entry.Blocked)
            {
                if (entry.Delta > 0) TotalGoldEarned += entry.Delta;
                if (entry.Delta < 0) TotalGoldSpent  += -entry.Delta;
            }
        }
    }

    private static double Average(IReadOnlyList<Player> players, Func<Player, int> selector)
    {
        double sum = 0;
        foreach (var p in players) sum += selector(p);
        return sum / players.Count;
    }
}
