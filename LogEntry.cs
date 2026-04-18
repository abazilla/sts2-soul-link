namespace SoulLinkMod;

public enum LogEntryType { Health, Gold }

/// <summary>
/// One entry in the shared HP/gold change log.
/// </summary>
/// <param name="Type">Whether this is an HP or gold event.</param>
/// <param name="PlayerSlot">Slot index of the player that caused the change (0-based).</param>
/// <param name="Delta">HP or gold change. Negative = damage/spent, positive = heal/gained.</param>
/// <param name="MaxHpDelta">Max HP change. 0 for gold entries or pure current-HP changes.</param>
/// <param name="Source">
///   For HP entries: damage source label (e.g. "Jaw Worm", "Thorns", "Rest").
///   For blocked gold: the blocking relic name (e.g. "Ectoplasm").
///   Null otherwise.
/// </param>
/// <param name="Blocked">True when a gold gain was blocked by a relic (e.g. Ectoplasm).</param>
public record LogEntry(
    LogEntryType Type,
    int PlayerSlot,
    int Delta,
    int MaxHpDelta = 0,
    string? Source = null,
    bool Blocked = false
);
