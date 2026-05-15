using System.Collections.Generic;
using Godot;

namespace SoulLinkMod;

/// <summary>
/// Centralized coordinator for all HP/MaxHP state changes.
/// Guarantees atomic check-apply-mark to prevent race conditions between
/// local deterministic events and network messages.
/// </summary>
public static class SyncCoordinator
{
    private static readonly object _lock = new();

    // Dedup dictionaries - moved from HpSyncPatch/MaxHpSyncPatch
    private static readonly Dictionary<(int slot, int delta), int> _hpCancellations = new();
    private static readonly Dictionary<(int slot, int delta), int> _hpNetworkApplied = new();
    private static readonly Dictionary<(int slot, int delta), int> _maxHpCancellations = new();
    private static readonly Dictionary<(int slot, int delta), int> _maxHpNetworkApplied = new();

    /// <summary>
    /// Atomically apply HP delta. Returns canonical value to write, or null if already applied.
    /// </summary>
    /// <param name="slot">Player slot</param>
    /// <param name="delta">HP change amount</param>
    /// <param name="isFromNetwork">True if this is from a network message, false if local event</param>
    /// <param name="inCombat">Whether currently in combat (affects scaling)</param>
    /// <param name="playerCount">Number of players (affects scaling)</param>
    /// <param name="source">Source label for logging</param>
    /// <returns>Canonical HP value to write, or null if change was already applied</returns>
    public static int? TryApplyHpDelta(int slot, int delta, bool isFromNetwork, bool inCombat, int playerCount, string? source)
    {
        lock (_lock)
        {
            // Check if already applied by the other path
            if (isFromNetwork)
            {
                if (TryConsume(_hpCancellations, slot, delta))
                {
                    SoulLinkLog.Debug($"[SyncCoordinator] HP delta already applied locally, skipping network: slot={slot} delta={delta}");
                    return null;
                }
            }
            else
            {
                if (TryConsume(_hpNetworkApplied, slot, delta))
                {
                    SoulLinkLog.Debug($"[SyncCoordinator] HP delta already applied by network, redirecting: slot={slot} delta={delta}");
                    return SoulLinkSession.CurrentHp;
                }
            }

            // Apply to canonical state
            int canonical = SoulLinkSession.ApplyHpDelta(delta, inCombat, playerCount, slot, source);

            // Mark as applied for the OTHER path to check later
            if (isFromNetwork)
                Enqueue(_hpNetworkApplied, slot, delta);
            else
                Enqueue(_hpCancellations, slot, delta);

            SoulLinkLog.Debug($"[SyncCoordinator] HP applied: slot={slot} delta={delta} isNetwork={isFromNetwork} canonical={canonical}");
            return canonical;
        }
    }

    /// <summary>
    /// Atomically apply MaxHP delta. Returns true if applied, false if already applied.
    /// Canonical values are written directly to SoulLinkSession.
    /// </summary>
    public static bool TryApplyMaxHpDelta(int slot, int delta, bool isFromNetwork, bool inCombat, int playerCount, string? source)
    {
        lock (_lock)
        {
            // Check if already applied
            if (isFromNetwork)
            {
                if (TryConsume(_maxHpCancellations, slot, delta))
                {
                    SoulLinkLog.Debug($"[SyncCoordinator] MaxHP delta already applied locally, skipping network: slot={slot} delta={delta}");
                    return false;
                }
            }
            else
            {
                if (TryConsume(_maxHpNetworkApplied, slot, delta))
                {
                    SoulLinkLog.Debug($"[SyncCoordinator] MaxHP delta already applied by network: slot={slot} delta={delta}");
                    return false;
                }
            }

            // Apply to canonical state
            SoulLinkSession.ApplyMaxHpDelta(delta, inCombat, playerCount, slot, source);

            // Mark as applied for the OTHER path to check later
            if (isFromNetwork)
                Enqueue(_maxHpNetworkApplied, slot, delta);
            else
                Enqueue(_maxHpCancellations, slot, delta);

            SoulLinkLog.Debug($"[SyncCoordinator] MaxHP applied: slot={slot} delta={delta} isNetwork={isFromNetwork}");
            return true;
        }
    }

    /// <summary>
    /// Get current canonical HP value (thread-safe read).
    /// </summary>
    public static int GetCanonicalHp()
    {
        lock (_lock)
        {
            return SoulLinkSession.CurrentHp;
        }
    }

    /// <summary>
    /// Get current canonical MaxHP value (thread-safe read).
    /// </summary>
    public static int GetCanonicalMaxHp()
    {
        lock (_lock)
        {
            return SoulLinkSession.MaxHp;
        }
    }

    /// <summary>
    /// Clear all dedup state. Called on session reset.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _hpCancellations.Clear();
            _hpNetworkApplied.Clear();
            _maxHpCancellations.Clear();
            _maxHpNetworkApplied.Clear();
            SoulLinkLog.Debug("[SyncCoordinator] Reset");
        }
    }

    // Helper: try to consume a dedup entry (returns true if found and consumed)
    private static bool TryConsume(Dictionary<(int, int), int> dict, int slot, int delta)
    {
        var key = (slot, delta);
        if (!dict.TryGetValue(key, out var count) || count == 0)
            return false;
        if (count == 1)
            dict.Remove(key);
        else
            dict[key] = count - 1;
        return true;
    }

    // Helper: enqueue a dedup entry
    private static void Enqueue(Dictionary<(int, int), int> dict, int slot, int delta)
    {
        var key = (slot, delta);
        dict.TryGetValue(key, out var count);
        dict[key] = count + 1;
    }
}
