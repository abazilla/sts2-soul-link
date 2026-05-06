using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.Examples;

/// <summary>
/// Example INetAction implementation: synchronized gold change.
///
/// Demonstrates the INetAction pattern for deterministic multiplayer state changes.
/// This is a reference implementation showing:
/// - Serialization/deserialization of action data
/// - Feature flag checking before execution
/// - Deterministic effect application
///
/// Phase 1: This is a scaffold/example only. Actual gold sync still uses GoldSyncPatch.
/// Phase 2+: Replace direct patching with action-based sync when NetworkedActions flag is enabled.
/// </summary>
public struct GoldChangeAction : INetAction
{
    /// <summary>
    /// Amount of gold to add (positive) or remove (negative).
    /// </summary>
    public int DeltaGold;

    /// <summary>
    /// Player slot that initiated this gold change.
    /// </summary>
    public int PlayerSlot;

    /// <summary>
    /// Source/reason for the gold change (for logging).
    /// Null if unknown or not applicable.
    /// </summary>
    public string? Source;

    /// <summary>
    /// Whether this gold change was blocked (e.g., by Ectoplasm).
    /// If true, Execute() should log but not apply the change.
    /// </summary>
    public bool WasBlocked;

    public bool ShouldBroadcast => true; // All clients need to see gold changes
    public NetTransferMode Mode => NetTransferMode.Reliable; // Critical state change
    public LogLevel LogLevel => LogLevel.Debug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(DeltaGold);
        writer.WriteInt(PlayerSlot);

        // Optional string: use bool guard for null safety
        bool hasSource = Source != null;
        writer.WriteBool(hasSource);
        if (hasSource)
        {
            writer.WriteString(Source!);
        }

        writer.WriteBool(WasBlocked);
    }

    public void Deserialize(PacketReader reader)
    {
        DeltaGold = reader.ReadInt();
        PlayerSlot = reader.ReadInt();

        bool hasSource = reader.ReadBool();
        Source = hasSource ? reader.ReadString() : null;

        WasBlocked = reader.ReadBool();
    }

    public void Execute(INetActionContext context)
    {
        // Check feature flags
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.NetworkedActions))
        {
            // Fall back to legacy sync (GoldSyncPatch handles this)
            return;
        }

        if (!FeatureFlagManager.IsEnabled(FeatureFlag.GoldSharing))
        {
            // Gold sharing disabled, skip
            return;
        }

        // Get run state
        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count == 0)
        {
            GD.PrintErr($"[SoulLink][GoldChangeAction] Execute failed: no run state");
            return;
        }

        if (PlayerSlot < 0 || PlayerSlot >= runState.Players.Count)
        {
            GD.PrintErr($"[SoulLink][GoldChangeAction] Execute failed: invalid player slot {PlayerSlot}");
            return;
        }

        // Log the action
        string sourceStr = Source != null ? $" (source: {Source})" : "";
        string blockedStr = WasBlocked ? " [BLOCKED]" : "";
        GD.Print($"[SoulLink][GoldChangeAction] Execute: player={PlayerSlot} delta={DeltaGold}{sourceStr}{blockedStr} isLocal={context.IsLocal}");

        if (WasBlocked)
        {
            // Blocked changes are logged but not applied
            return;
        }

        // Apply gold change deterministically
        // (Phase 2: integrate with SoulLinkSession.ApplyGoldDelta)
        // For Phase 1 scaffold, this is a no-op example

        // Future implementation would:
        // 1. Update SoulLinkSession canonical gold
        // 2. Mirror to all player objects
        // 3. Refresh UI panels
        // 4. Add to combat log
    }
}
