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

    // ── HP heal coordination ──────────────────────────────────────────────────

    // When MaxHP increases, the game fires a follow-up CurrentHp write whose delta
    // equals the already-scaled MaxHp gain. We store that amount here so ApplyHpDelta
    // can apply it directly without scaling it a second time.
    // Cleared on every ApplyHpDelta call to prevent stale values across events.
    private static int _pendingMaxHpHeal;

    // True once the first combat has started. Before this point we are in the
    // "initialization phase" (Neow's heal, game startup), where out-of-combat
    // heals must NOT be divided by player count — Neow only fires once, not per player.
    // After the first combat, campfire/event heals fire once per player, so scaling applies.
    private static bool _initPhaseComplete;

    // HealInternal receives the unclamped heal amount before SetCurrentHpInternal clamps it
    // to MaxHp. We stash it here so ApplyHpDelta can scale the true intended heal rather
    // than the already-clamped delta that arrives via the CurrentHp setter.
    // Cleared on every ApplyHpDelta call to prevent stale values across events.
    public static int PendingUnclampedHeal { get; set; }

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

        // Detect save-load vs fresh run.
        // The game pre-initializes all creatures to MaxHp before Launch() fires, even on a
        // brand-new run, so "CurrentHp > 0" is not a reliable fresh-run signal.
        // Instead we combine two checks:
        //   1. Any player has CurrentHp < MaxHp (they took damage since run start), OR
        //      MaxHp > Character.StartingHp (max-HP upgrades were obtained).
        //   2. Gold is no longer the starting amount (99) — meaning Neow or combat has fired.
        // A fresh run passes both tests (all players at full base HP, gold still 99).
        // A mid-run save fails at least one of them.
        const int StartingGold = 99;
        bool isSaveLoad = runState.Players[0].Gold != StartingGold;
        if (!isSaveLoad)
        {
            foreach (var p in runState.Players)
            {
                int chp = p.Creature.CurrentHp;
                int mhp = BestMaxHp(p);
                if ((chp > 0 && chp < mhp) || mhp > p.Character.StartingHp)
                {
                    isSaveLoad = true;
                    break;
                }
            }
        }

        int sharedMaxHp = RoundedAverage(runState.Players, p => BestMaxHp(p));

        int sharedCurrentHp;
        if (isSaveLoad)
        {
            // Restore CurrentHp from save. All players should have the same HP due to soul link,
            // but average in case of any transient divergence. Fall back to MaxHp if 0.
            sharedCurrentHp = RoundedAverage(runState.Players,
                p => p.Creature.CurrentHp > 0 ? p.Creature.CurrentHp : BestMaxHp(p));
            GD.Print($"[SoulLink] Save-load detected. Restoring MaxHp={sharedMaxHp}, CurrentHp={sharedCurrentHp}");
            // A save-load implies the run is past initialization — combat has already happened.
            _initPhaseComplete = true;
        }
        else
        {
            // Fresh run: the game pre-initializes creatures to full HP, so we match that.
            // Neow will immediately reset HP to 0 then heal to the ascension-scaled value
            // (e.g. 64 for A2 Ironclad). During _initPhaseComplete=false, those heals are
            // applied without playerCount scaling — Neow fires per-player but the reset+heal
            // pairs resolve correctly to the intended starting HP.
            sharedCurrentHp = sharedMaxHp;
            _initPhaseComplete = false;
            GD.Print($"[SoulLink] Fresh run — init phase active. MaxHp={sharedMaxHp}, CurrentHp={sharedCurrentHp} (Neow will adjust to ascension-scaled value).");
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
        IsActive          = false;
        CurrentHp         = 0;
        MaxHp             = 0;
        Gold              = 0;
        _pendingMaxHpHeal  = 0;
        _initPhaseComplete = false;
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
        int pendingHeal = _pendingMaxHpHeal;
        _pendingMaxHpHeal = 0;  // always consume — prevents stale values leaking across events

        int unclampedHeal = PendingUnclampedHeal;
        PendingUnclampedHeal = 0;  // always consume

        int delta = rawDelta;

        // The first time a combat HP change fires, we know initialization is complete.
        // From this point on, campfire/event heals fire once per player and must be scaled.
        if (inCombat) _initPhaseComplete = true;

        if (delta > 0 && !inCombat)
        {
            if (pendingHeal > 0)
            {
                // This heal is the follow-up to a MaxHP gain. The game already computed
                // the delta using our redirected (scaled) MaxHp value, so the amount is
                // already correct — don't scale it again.
                delta = pendingHeal;
            }
            else if (_initPhaseComplete && playerCount > 1)
            {
                // Normal out-of-combat heal (campfire, event, etc.): scale by 1/playerCount
                // so each player's heal contributes a proportional share to the shared pool.
                // Use the unclamped heal amount from HealInternal if available, so that
                // near-full players don't get double-penalized (game clamps to MaxHp before
                // the setter fires, then we'd divide the already-reduced delta).
                int baseForScaling = unclampedHeal > 0 ? unclampedHeal : rawDelta;
                delta = Math.Max(1, (int)Math.Round((double)baseForScaling / playerCount, MidpointRounding.AwayFromZero));
            }
            // If !_initPhaseComplete: this is the Neow/initialization heal. Apply in full —
            // it fires only once (for one player), so no playerCount division needed.
        }

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
            scaledDelta = Math.Max(1, (int)Math.Round((double)delta / playerCount, MidpointRounding.AwayFromZero));

        MaxHp = Math.Max(1, MaxHp + scaledDelta);
        CurrentHp = Math.Min(CurrentHp, MaxHp);

        // When MaxHP increases, the game fires a follow-up CurrentHp write equal to
        // the scaled delta (it reads back the already-redirected MaxHp value to compute
        // the heal amount). Mark that the next out-of-combat heal should NOT be scaled
        // a second time — it's already been scaled here.
        if (scaledDelta > 0)
            _pendingMaxHpHeal = scaledDelta;

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

    private static int RoundedAverage(IReadOnlyList<Player> players, Func<Player, int> selector)
    {
        double sum = 0;
        foreach (var p in players) sum += selector(p);
        return (int)Math.Round(sum / players.Count, MidpointRounding.AwayFromZero);
    }
}
