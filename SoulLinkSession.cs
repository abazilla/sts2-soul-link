using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod;

/// <summary>
/// Holds the canonical shared state for a Soul Link session:
/// one HP pool shared across all players, and gold tracking per the active GoldMode.
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

    /// <summary>Canonical gold for SharedPool mode. Not used in Default or SplitByPlayer modes.</summary>
    public static int Gold       { get; private set; }

    /// <summary>
    /// True when the current run was restored from a save (vs freshly launched).
    /// Set in OnRunStart; consulted by ReinitSharedGold to decide whether each player's
    /// stored Gold field is a mirrored pool value (save-load) or an independent per-player
    /// value (fresh run, before SharedPool took over).
    /// </summary>
    public static bool IsSaveLoadedRun { get; private set; }

    /// <summary>Per-player canonical gold for SplitByPlayer mode (indexed by player slot).</summary>
    private static readonly int[] _playerGold = new int[4];

    /// <summary>Returns the canonical gold for a specific player slot (SplitByPlayer mode).</summary>
    public static int GetPlayerGold(int slot) =>
        slot >= 0 && slot < _playerGold.Length ? _playerGold[slot] : 0;

    // ── Active run settings ───────────────────────────────────────────────────

    /// <summary>
    /// The settings locked in for the current run.
    /// Set at run start from the host's SoulLinkSettings (fresh run) or from
    /// the saved run-settings file (save-load). Broadcast to clients via
    /// SoulLinkSettingsSyncMessage so all peers use identical values.
    /// </summary>
    public static SoulLinkRunSettings ActiveRunSettings { get; internal set; }

    /// <summary>
    /// Holds run settings received from the host via SoulLinkSettingsSyncMessage
    /// before this peer's own RunManager.Launch() has fired (client timing).
    /// OnRunStart() checks this and prefers it over local settings when set.
    /// </summary>
    internal static SoulLinkRunSettings? PendingSyncedRunSettings;

    // ── HP heal coordination ──────────────────────────────────────────────────

    // When MaxHP increases, the game fires a follow-up CurrentHp write whose delta
    // equals the already-scaled MaxHp gain. We store that amount here so ApplyHpDelta
    // can apply it directly without scaling it a second time.
    // Cleared on every ApplyHpDelta call to prevent stale values across events.
    private static int _pendingMaxHpHeal;

    // When MaxHp decreases below CurrentHp, ApplyMaxHpDelta clamps CurrentHp immediately.
    // The game then fires a follow-up CurrentHp setter with the same clamp amount as a
    // negative delta. We store the clamp here so ApplyHpDelta can recognise and skip it
    // (CurrentHp is already correct — no logging, no double-apply).
    // Cleared on every ApplyHpDelta call.
    private static int _pendingHpClamp;

    // True once the first combat has started. Before this point we are in the
    // "initialization phase" (Neow's heal, game startup), where out-of-combat
    // heals must NOT be divided by player count — Neow only fires once, not per player.
    // After the first combat, campfire/event heals fire once per player, so scaling applies.
    private static bool _initPhaseComplete;

    // Per-slot init-seed tracking. Vanilla character setup writes each player's CurrentHp
    // twice at run start: value=0, then value=postAscensionStartingHp. Each slot's writes
    // are recorded in _initialCurrentHpSeeds and the slot is marked seeded once we see its
    // positive value. Canonical CurrentHp is not touched during this collection — once
    // every slot has reported, CurrentHp is finalised as the average of the seeds. This
    // matches "Pool CurrentHp = avg(player.Creature.CurrentHp after ascension)" and works
    // for mixed-character parties (Ironclad 80, Silent 70, Defect 75, …).
    // MaxHp does NOT need this: pool MaxHp is seeded in OnRunStart from creature averages
    // (creatures already carry their character MaxHp before any ascension writes fire), and
    // vanilla's char/ascension init never writes MaxHp through the public setter (uses
    // reflection on the backing field), so MaxHpSyncPatch only sees real events anyway.
    private static bool[] _slotCurrentHpSeeded   = Array.Empty<bool>();
    private static int[]  _initialCurrentHpSeeds = Array.Empty<int>();

    /// <summary>
    /// True once the first combat has started. Used to skip network sync during Neow
    /// (which is deterministic and doesn't need sync).
    /// </summary>
    public static bool IsInitPhaseComplete => _initPhaseComplete;

    internal static void MarkInitPhaseComplete() => _initPhaseComplete = true;

    /// <summary>
    /// Init-phase HP setter: overwrites canonical CurrentHp directly with the given value
    /// (no delta math, no scaling). Used during the run-start window where vanilla performs
    /// per-peer initialization writes (ascension scaling, character setup, Neow setup) whose
    /// ordering and intermediate values are not knowable in advance — trusting the final
    /// written value is more robust than tracking deltas.
    ///
    /// Emits a log entry for the change unless this is the first 0 → starting CurrentHp
    /// seed of a fresh run (vanilla's character-setup heal, not a player-visible event).
    /// </summary>
    public static void OverwriteCanonicalHp(int value, int playerSlot, string? source)
    {
        int v = Math.Max(0, value);

        // Per-slot init-seed collection. Vanilla writes each player's CurrentHp twice
        // during ascension setup (0 then post-ascension HP); record each slot's positive
        // value. Canonical CurrentHp tracks the latest write (so HpSyncPatch's mirror loop
        // keeps every creature in sync with what vanilla wrote). Once every slot has been
        // seeded, override canonical = avg(seeds) so the shared pool reflects the
        // post-ascension HP across all characters (works for mixed-character parties).
        if (playerSlot >= 0 && playerSlot < _slotCurrentHpSeeded.Length && !_slotCurrentHpSeeded[playerSlot])
        {
            CurrentHp = Math.Min(v, MaxHp);
            _initialCurrentHpSeeds[playerSlot] = v;
            if (v > 0) _slotCurrentHpSeeded[playerSlot] = true;

            bool allSeeded = true;
            for (int i = 0; i < _slotCurrentHpSeeded.Length; i++)
                if (!_slotCurrentHpSeeded[i]) { allSeeded = false; break; }
            if (allSeeded)
            {
                double sum = 0;
                for (int i = 0; i < _initialCurrentHpSeeds.Length; i++) sum += _initialCurrentHpSeeds[i];
                int finalHp = ActiveRunSettings.StartingHpMode == StartingHpMode.Additive
                    ? (int)Math.Min(sum, int.MaxValue)
                    : (int)Math.Round(sum / _initialCurrentHpSeeds.Length, MidpointRounding.AwayFromZero);
                CurrentHp = Math.Min(finalHp, MaxHp);

                // Push pool MaxHp + CurrentHp to all creatures now that ascension-scaled
                // per-slot writes have landed. Done here (not in OnRunStart) because pushing
                // pool MaxHp before vanilla's CurrentHp init causes vanilla to write
                // CurrentHp = creature.MaxHp = pool MaxHp, bypassing ascension scaling.
                var rs = RunManager.Instance?.DebugOnlyGetState();
                if (rs != null)
                {
                    SoulLinkMod.ApplyingCanonical = true;
                    try
                    {
                        foreach (var player in rs.Players)
                        {
                            if (player.Creature.MaxHp != MaxHp)
                                player.Creature.SetMaxHp(MaxHp);
                            if (player.Creature.CurrentHp != CurrentHp)
                                player.Creature.SetCurrentHp(CurrentHp);
                        }
                    }
                    finally { SoulLinkMod.ApplyingCanonical = false; }
                }
            }
            return;
        }

        int previous = CurrentHp;
        CurrentHp = Math.Min(v, MaxHp);

        int delta = CurrentHp - previous;
        if (delta != 0)
            AddEntry(new LogEntry(LogEntryType.Health, playerSlot, delta, 0, source));
    }

    // HealInternal receives the unclamped heal amount before SetCurrentHpInternal clamps it
    // to MaxHp. We stash it here so ApplyHpDelta can scale the true intended heal rather
    // than the already-clamped delta that arrives via the CurrentHp setter.
    // Cleared on every ApplyHpDelta call to prevent stale values across events.
    public static int PendingUnclampedHeal { get; set; }

    // Set by SourceCapturePatch hooks immediately before a HP or gold setter fires.
    // Consumed (read + cleared) by HpSyncPatch / MaxHpSyncPatch / GoldSyncPatch when
    // building the LogEntry. Falls back to CurrentRoomSource, then a generic label.
    public static string? PendingSource { get; set; }

    /// <summary>
    /// True while a deterministic per-peer heal is firing (e.g. BurningBlood.AfterCombatVictory,
    /// AncientEventModel.BeforeEventStarted act-transition heal). Each player's instance fires
    /// locally, producing N CurrentHp setter calls. ApplyHpDelta lets only the first positive
    /// delta through and suppresses duplicates so the pool gains the heal exactly once.
    /// </summary>
    public static bool PerPeerDeterministicHeal { get; set; }

    // Tracks whether a heal has already landed during the current PerPeerDeterministicHeal window.
    // Set true on first positive ApplyHpDelta while flag is active; reset by BeginPerPeerHeal.
    private static bool _deterministicHealApplied;

    /// <summary>
    /// Call before setting PerPeerDeterministicHeal = true to reset the dedup tracker.
    /// </summary>
    public static void BeginPerPeerHeal()
    {
        _deterministicHealApplied = false;
        PerPeerDeterministicHeal = true;
    }

    // Set when entering a room and cleared on exit. Used as a persistent source label
    // for the whole room (e.g. "Neow", "Rest Site", "Shop") so multi-step events
    // (lose HP AND gold) all get labelled correctly without needing per-change hooks.
    public static string? CurrentRoomSource { get; set; }

    // ── Change log ────────────────────────────────────────────────────────────

    private const int LogCapacity = 20;
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
            SoulLinkLog.Debug($"Player {p.Character.GetType().Name}: Character.StartingHp={p.Character.StartingHp}, Creature.MaxHp={p.Creature.MaxHp}, Creature.CurrentHp={p.Creature.CurrentHp}");

        // On a fresh run, Creature.MaxHp may be 0 on the host at Launch() time — fall back
        // to Character.StartingHp (the static data value) in that case.
        // On a save-load, Creature.MaxHp holds the saved value with accumulated in-run gains.
        static int BestMaxHp(Player p) =>
            p.Creature.MaxHp > 0 ? p.Creature.MaxHp : p.Character.StartingHp;

        // Detect save-load vs fresh run.
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

        // Allocate per-slot seed arrays. Sized from player count so any party size works.
        int slotCount = runState.Players.Count;
        if (_slotCurrentHpSeeded.Length   != slotCount) _slotCurrentHpSeeded   = new bool[slotCount];
        if (_initialCurrentHpSeeds.Length != slotCount) _initialCurrentHpSeeds = new int[slotCount];

        // Determine which run settings to use BEFORE seeding so StartingHpMode can pick
        // between average and additive seeding.
        if (PendingSyncedRunSettings.HasValue)
        {
            ActiveRunSettings = PendingSyncedRunSettings.Value;
            PendingSyncedRunSettings = null;
            SoulLinkLog.Debug("Applied pre-received run settings from host (early sync message).");
        }
        else if (isSaveLoad)
        {
            SoulLinkRunSettings? saved = SoulLinkSettings.LoadRunSettings();
            ActiveRunSettings = saved ?? SoulLinkSettings.Instance.ToRunSettings();
            if (saved == null)
                SoulLinkLog.Error("No run settings file found on save-load; using current settings.");
        }
        else
        {
            ActiveRunSettings = SoulLinkSettings.Instance.ToRunSettings();
        }

        // Always re-save so re-hosting from this save will see the correct settings.
        SoulLinkSettings.SaveRunSettings(ActiveRunSettings);
        SoulLinkLog.Debug($"Run settings: HpMode={ActiveRunSettings.HpMode}, StartingHpMode={ActiveRunSettings.StartingHpMode}, GoldMode={ActiveRunSettings.GoldMode}");

        bool additive = ActiveRunSettings.StartingHpMode == StartingHpMode.Additive;
        int sharedMaxHp;
        int sharedCurrentHp;
        if (isSaveLoad)
        {
            // Save-load: every player's Creature already holds the SHARED POOL MaxHp/CurrentHp
            // (sync writes the pool back to all players during play, and the save persists it).
            // So take the max across players — re-aggregating with Sum would compound (N× pool)
            // every rehost. Max (rather than [0]) tolerates any single-player desync at save time.
            sharedMaxHp = 0;
            sharedCurrentHp = 0;
            for (int i = 0; i < slotCount; i++)
            {
                int mhp = BestMaxHp(runState.Players[i]);
                int chp = runState.Players[i].Creature.CurrentHp;
                if (chp <= 0) chp = mhp;
                if (mhp > sharedMaxHp) sharedMaxHp = mhp;
                if (chp > sharedCurrentHp) sharedCurrentHp = chp;
            }
            SoulLinkLog.Debug($"Save-load detected. Restoring MaxHp={sharedMaxHp}, CurrentHp={sharedCurrentHp} (additive={additive})");
            _initPhaseComplete = true;
            Array.Fill(_slotCurrentHpSeeded, true);
            for (int i = 0; i < slotCount; i++)
                _initialCurrentHpSeeds[i] = runState.Players[i].Creature.CurrentHp;
        }
        else
        {
            // Fresh run: seed pool MaxHp from the chosen aggregate of each player's character MaxHp
            // (creatures already carry their starting MaxHp before any ascension scaling
            // touches CurrentHp). CurrentHp is deferred — captured per-slot by HpSyncPatch
            // during vanilla's ascension writes, then aggregated once every slot has reported.
            sharedMaxHp = additive
                ? RoundedSum(runState.Players, p => BestMaxHp(p))
                : RoundedAverage(runState.Players, p => BestMaxHp(p));
            sharedCurrentHp = 0;
            _initPhaseComplete = false;
            Array.Clear(_slotCurrentHpSeeded, 0, _slotCurrentHpSeeded.Length);
            Array.Clear(_initialCurrentHpSeeds, 0, _initialCurrentHpSeeds.Length);
            SoulLinkLog.Debug($"Fresh run — pool MaxHp seeded to {sharedMaxHp} (additive={additive}); CurrentHp deferred until vanilla's per-peer ascension writes finish.");
        }

        // Vanilla HpMode never routes through ApplyHpDelta, so the in-combat trigger that
        // flips _initPhaseComplete never fires. Mark complete now so gold log entries
        // (and any other non-HP entries) aren't suppressed by AddEntry's gate.
        if (ActiveRunSettings.HpMode == HpMode.Vanilla)
        {
            _initPhaseComplete = true;
            Array.Fill(_slotCurrentHpSeeded, true);
            for (int i = 0; i < slotCount; i++)
                _initialCurrentHpSeeds[i] = runState.Players[i].Creature.CurrentHp;
        }

        MaxHp     = sharedMaxHp;
        CurrentHp = sharedCurrentHp;

        // Always initialize _playerGold from current player values as a defensive baseline.
        // This prevents stale zeros if ActiveRunSettings.GoldMode changes after OnRunStart
        // (e.g. the client's local default differs from the host's setting that arrives shortly after).
        for (int i = 0; i < runState.Players.Count && i < _playerGold.Length; i++)
            _playerGold[i] = runState.Players[i].Gold;
        for (int i = runState.Players.Count; i < _playerGold.Length; i++)
            _playerGold[i] = 0;

        IsSaveLoadedRun = isSaveLoad;

        switch (ActiveRunSettings.GoldMode)
        {
            case GoldSharingMode.SharedPool:
                // Save-load: each player.Gold already mirrors the saved pool value
                // (ApplyToAllPlayers wrote it before save), so take a single player's value.
                // Fresh run: pool = sum of every player's starting gold (e.g. 99 * N).
                Gold = isSaveLoad
                    ? runState.Players[0].Gold
                    : RoundedSum(runState.Players, p => p.Gold);
                break;
            default: // Default and SplitByPlayer: Gold field unused
                Gold = 0;
                break;
        }

        // On a save-load keep cumulative totals — the run is continuing, not starting fresh.
        if (!isSaveLoad)
        {
            _log.Clear();
            TotalDamageTaken   = 0;
            TotalHealingGained = 0;
            TotalGoldEarned    = 0;
            TotalGoldSpent     = 0;
        }

        // Save-load: write canonical back to all creatures so vanilla picks up restored state.
        // Fresh run: skip — pool MaxHp push deferred to OverwriteCanonicalHp's allSeeded
        // finalisation so vanilla's per-slot ascension-scaled CurrentHp writes are not
        // clobbered by `value = creature.MaxHp = pool MaxHp`.
        if (isSaveLoad)
            ApplyToAllPlayers(runState);
        IsActive = true;
    }

    /// <summary>Called when the run ends (win or loss). Clears all state.</summary>
    public static void OnRunEnd()
    {
        IsActive                  = false;
        CurrentHp                 = 0;
        MaxHp                     = 0;
        Gold                      = 0;
        for (int i = 0; i < _playerGold.Length; i++)
            _playerGold[i] = 0;
        _pendingMaxHpHeal         = 0;
        _pendingHpClamp           = 0;
        _initPhaseComplete        = false;
        Array.Clear(_slotCurrentHpSeeded, 0, _slotCurrentHpSeeded.Length);
        Array.Clear(_initialCurrentHpSeeds, 0, _initialCurrentHpSeeds.Length);
        PendingSource             = null;
        PerPeerDeterministicHeal  = false;
        _deterministicHealApplied = false;
        CurrentRoomSource         = null;
        ActiveRunSettings         = default;
        PendingSyncedRunSettings  = null;
        IsSaveLoadedRun           = false;
        _log.Clear();
        TotalDamageTaken   = 0;
        TotalHealingGained = 0;
        TotalGoldEarned    = 0;
        TotalGoldSpent     = 0;
        // Note: soullink_run.json is intentionally NOT deleted here.
        // If the host re-starts the game and loads this save, the file must
        // still exist so OnRunStart can restore the original run settings.
    }

    // ── Apply HP delta ────────────────────────────────────────────────────────

    /// <summary>
    /// Called from HpSyncPatch when any player's CurrentHp is being written.
    /// Updates the canonical pool, logs the entry, and writes back to all players.
    /// Returns the value that should be written to the triggering player's HP
    /// (the canonical value).
    ///
    /// All heals apply at full nominal value — split/divided heal scaling has been
    /// removed (the additive starting-HP mode covers the difficulty axis it used to).
    /// </summary>
    public static int ApplyHpDelta(int rawDelta, bool inCombat, int playerCount, int playerSlot, string? source = null)
    {
        int pendingHeal = _pendingMaxHpHeal;
        _pendingMaxHpHeal = 0;  // always consume — prevents stale values leaking across events

        int pendingClamp = _pendingHpClamp;
        _pendingHpClamp = 0;  // always consume

        PendingUnclampedHeal = 0;  // always consume even though we no longer scale

        // This is the follow-up CurrentHp write caused by MaxHp dropping below CurrentHp.
        // ApplyMaxHpDelta already clamped CurrentHp — nothing to do here, and nothing to log.
        if (pendingClamp > 0 && rawDelta == -pendingClamp)
            return CurrentHp;

        int delta = rawDelta;

        // Per-peer deterministic heal dedup: only the first peer's positive delta
        // lands on the pool. Subsequent peers' identical heals are suppressed.
        if (PerPeerDeterministicHeal && delta > 0)
        {
            if (_deterministicHealApplied)
            {
                SoulLinkLog.Debug($"[ApplyHpDelta] Suppressing duplicate per-peer heal: delta={delta} pool={CurrentHp}");
                return CurrentHp;
            }
            _deterministicHealApplied = true;
        }

        // The first time a combat HP change fires, we know initialization is complete.
        if (inCombat) _initPhaseComplete = true;

        // Heal-to-full follow-up after MaxHP gain (e.g. Lee's Waffle).
        // Per-player vanilla heals to its own MaxHp; in SharedPool the intent is
        // "fill the pool", so heal the pool to MaxHp regardless of the per-player
        // delta the game computed.
        if (delta > 0 && !inCombat && pendingHeal > 0 && rawDelta > pendingHeal)
        {
            delta = MaxHp - CurrentHp;
        }

        CurrentHp = Math.Clamp(CurrentHp + delta, 0, MaxHp);

        AddEntry(new LogEntry(LogEntryType.Health, playerSlot, delta, 0, source));
        return CurrentHp;
    }

    /// <summary>
    /// Revive heal: overwrites the canonical pool with the given absolute value
    /// (clamped to MaxHp) and emits a log entry. Represents a single consume event
    /// (Fairy/Lizard), not a per-peer heal.
    /// </summary>
    public static void ReviveCanonicalHp(int healed, int playerSlot, string? source)
    {
        int previous = CurrentHp;
        CurrentHp = Math.Clamp(healed, 0, MaxHp);
        int delta = CurrentHp - previous;
        if (delta != 0)
            AddEntry(new LogEntry(LogEntryType.Health, playerSlot, delta, 0, source));
    }

    /// <summary>
    /// Called from MaxHpSyncPatch. Updates canonical max HP (and clamps current HP).
    /// </summary>
    public static void ApplyMaxHpDelta(int delta, bool inCombat, int playerCount, int playerSlot, string? source = null)
    {
        // MaxHP changes apply at full nominal value — split-by-player scaling removed.
        int oldCurrentHp = CurrentHp;
        MaxHp = Math.Max(1, MaxHp + delta);
        CurrentHp = Math.Min(CurrentHp, MaxHp);

        if (delta > 0)
            _pendingMaxHpHeal = delta;

        // If CurrentHp was clamped down, store the clamp amount so ApplyHpDelta can
        // recognise and silently absorb the follow-up CurrentHp setter the game fires.
        if (CurrentHp < oldCurrentHp)
            _pendingHpClamp = oldCurrentHp - CurrentHp;

        AddEntry(new LogEntry(LogEntryType.Health, playerSlot, 0, delta, source));
    }

    // ── Apply Gold delta ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets canonical gold directly without logging. Called by GoldSyncHandler on the
    /// receiving peer to mirror the sender's canonical value.
    /// </summary>
    public static void SetGoldDirect(int canonical, int playerSlot = 0)
    {
        if (ActiveRunSettings.GoldMode == GoldSharingMode.SplitByPlayer)
        {
            if (playerSlot >= 0 && playerSlot < _playerGold.Length)
                _playerGold[playerSlot] = Math.Max(0, canonical);
        }
        else
        {
            Gold = Math.Max(0, canonical);
        }
    }

    /// <summary>
    /// Adds a gold log entry (and updates cumulative totals) without changing canonical gold.
    /// Called by GoldSyncHandler on the receiving peer to log the remote player's change.
    /// Delta=0 is a no-op (used for the initial sync-at-run-start message).
    /// </summary>
    public static void LogGoldEntry(int delta, int playerSlot, string? source)
    {
        if (delta == 0) return;
        AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, delta, Source: source));
    }

    /// <summary>
    /// Called from GoldSyncPatch. Updates the canonical gold pool per the active GoldMode.
    /// If blocked (e.g. Ectoplasm), the delta is logged but NOT applied.
    /// Returns the canonical gold value that should actually be written to the player.
    /// </summary>
    public static int ApplyGoldDelta(int delta, int playerCount, int playerSlot, bool blocked, string? blockSource = null, string? source = null)
    {
        int currentCanonical = ActiveRunSettings.GoldMode == GoldSharingMode.SplitByPlayer
            ? GetPlayerGold(playerSlot)
            : Gold;

        if (blocked)
        {
            AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, delta, 0, blockSource, Blocked: true));
            return currentCanonical;
        }

        switch (ActiveRunSettings.GoldMode)
        {
            case GoldSharingMode.SharedPool:
            {
                // One pool shared by all: gains and spends go directly to/from the pool.
                Gold = Math.Max(0, Gold + delta);
                AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, delta, Source: source));
                return Gold;
            }
            case GoldSharingMode.SplitByPlayer:
            {
                // Each player's own pool.
                // Gains are divided by playerCount so the windfall is shared proportionally.
                // Spends come only from the buyer's pool in full.
                int scaledDelta;
                if (delta > 0 && playerCount > 1)
                    scaledDelta = Math.Max(1, (int)Math.Round((double)delta / playerCount, MidpointRounding.AwayFromZero));
                else
                    scaledDelta = delta;

                // Update the triggering player's pool.
                _playerGold[playerSlot] = Math.Max(0, _playerGold[playerSlot] + scaledDelta);
                AddEntry(new LogEntry(LogEntryType.Gold, playerSlot, scaledDelta, Source: source));

                // For gains, proactively update all OTHER slots on this machine so the
                // mirror loop in GoldSyncPatch writes the correct canonical to proxy players.
                // The receiver machine will apply the same delta independently when it handles
                // the broadcast (and will log their own gain there).
                if (scaledDelta > 0)
                {
                    for (int i = 0; i < playerCount && i < _playerGold.Length; i++)
                    {
                        if (i == playerSlot) continue;
                        _playerGold[i] = Math.Max(0, _playerGold[i] + scaledDelta);
                    }
                }

                return _playerGold[playerSlot];
            }
            default:
                // Default mode — GoldSyncPatch returns early before calling here.
                return 0;
        }
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
            for (int i = 0; i < runState.Players.Count; i++)
            {
                var player = runState.Players[i];
                player.Creature.SetMaxHp(MaxHp);
                player.Creature.SetCurrentHp(CurrentHp);

                switch (ActiveRunSettings.GoldMode)
                {
                    case GoldSharingMode.SharedPool:
                        player.Gold = Gold;
                        break;
                    case GoldSharingMode.SplitByPlayer:
                        if (i < _playerGold.Length)
                            player.Gold = _playerGold[i];
                        break;
                    // Default: don't touch gold — STS2 manages it natively.
                }
            }
        }
        finally
        {
            SoulLinkMod.ApplyingCanonical = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads per-player gold from actual player objects. Called when GoldMode changes
    /// to SplitByPlayer after OnRunStart (settings sync timing), so _playerGold[] reflects
    /// what the game currently has rather than stale zeros.
    /// </summary>
    internal static void ReinitPlayerGold(RunState runState)
    {
        for (int i = 0; i < runState.Players.Count && i < _playerGold.Length; i++)
            _playerGold[i] = runState.Players[i].Gold;
        SoulLinkLog.Debug($"ReinitPlayerGold: P0={_playerGold[0]}, P1={_playerGold[1]}");
    }

    /// <summary>
    /// Re-seeds the shared gold pool from the local player data. Called when GoldMode
    /// switches to SharedPool after OnRunStart already ran with a different mode — in that
    /// case <see cref="Gold"/> was never initialized (it's 0). Reading from player objects
    /// gives the correct saved value because Default/SplitByPlayer modes don't overwrite
    /// player gold in ApplyToAllPlayers.
    /// </summary>
    internal static void ReinitSharedGold(RunState runState)
    {
        if (runState.Players.Count == 0) return;
        // Save-load: every player.Gold mirrors the saved pool — take a single value.
        // Fresh run / mid-run mode switch: each player holds an independent amount
        // (vanilla per-player or SplitByPlayer pots), so combine them into the pool.
        Gold = IsSaveLoadedRun
            ? runState.Players[0].Gold
            : RoundedSum(runState.Players, p => p.Gold);
        // Mirror canonical to every player so they're consistent.
        SoulLinkMod.ApplyingCanonical = true;
        try { foreach (var p in runState.Players) p.Gold = Gold; }
        finally { SoulLinkMod.ApplyingCanonical = false; }
        SoulLinkLog.Debug($"ReinitSharedGold: Gold={Gold}");
    }

    /// <summary>
    /// Attempts to broadcast current SoulLinkSettings to connected peers.
    /// Useful in the lobby so the client's read-only panel stays in sync with the host.
    /// No-op if NetService is unavailable.
    /// </summary>
    public static void TrySendSettingsSync()
    {
        try
        {
            var s = SoulLinkSettings.Instance;
            var msg = new SoulLinkSettingsSyncMessage
            {
                GoldMode     = (int)s.GoldMode,
                SharedLoseHp = s.SharedLoseHp,
                HpMode       = (int)s.HpMode,
                StartingHpMode = (int)s.StartingHpMode,
            };

            var net = RunManager.Instance?.NetService;
            if (net != null && net.IsConnected)
            {
                SoulLinkLog.Debug($"[SyncDiag] TrySendSettingsSync: SENDING via run net#{net.GetHashCode()} GoldMode={s.GoldMode} HpMode={s.HpMode} StartingHpMode={s.StartingHpMode} SharedLoseHp={s.SharedLoseHp}");
                net.SendMessage(msg);
                return;
            }

            if (Patches.LobbyNetCapture.IsAvailable)
            {
                SoulLinkLog.Debug($"[SyncDiag] TrySendSettingsSync: SENDING via lobby net#{Patches.LobbyNetCapture.Service!.GetHashCode()} GoldMode={s.GoldMode} HpMode={s.HpMode} StartingHpMode={s.StartingHpMode} SharedLoseHp={s.SharedLoseHp}");
                Patches.LobbyNetCapture.TrySend(msg);
                return;
            }

            if (net == null) SoulLinkLog.Debug("[SyncDiag] TrySendSettingsSync: net=null, lobby=null, skipping.");
            else SoulLinkLog.Debug($"[SyncDiag] TrySendSettingsSync: net.IsConnected=false (net#{net.GetHashCode()}), lobby=null, skipping.");
        }
        catch (Exception ex)
        {
            SoulLinkLog.Error($"TrySendSettingsSync failed: {ex.Message}");
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static void AddEntry(LogEntry entry)
    {
        // Init-phase events (Neow choices, gifts, downsides) DO log. The initial 0 →
        // starting HP/MaxHp seed writes are filtered upstream in OverwriteCanonical*
        // so vanilla's character-setup heal doesn't appear here.
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

    private static int RoundedSum(IReadOnlyList<Player> players, Func<Player, int> selector)
    {
        long sum = 0;
        foreach (var p in players) sum += selector(p);
        return (int)Math.Min(sum, int.MaxValue);
    }
}
