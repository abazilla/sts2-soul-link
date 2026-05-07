# INetAction System and Feature Flags

This document describes the INetAction contract system and feature flag infrastructure.

## Overview

### INetAction

`INetAction` is a contract for networked game actions that need deterministic synchronization across all multiplayer clients. It provides a structured alternative to direct Harmony patching with custom messages.

**Key differences from INetMessage:**
- **INetAction**: Represents state-changing operations (gain gold, heal, take damage)
- **INetMessage**: Represents notifications or data transfers (sync settings, update UI)

### Feature Flags

The feature flag system (`FeatureFlagManager`) allows runtime enabling/disabling of mod features with three scopes:
- **Global**: Persisted across runs and sessions
- **Run**: Active for the current run only
- **Session**: Active for the current play session only

## Implementation Status

### Files

1. **INetAction.cs** - Core action contract and context interface
2. **NetActionContext.cs** - Default implementation of action execution context
3. **NetActionService.cs** - Service for sending/receiving actions
4. **NetActionMessage.cs** - Generic wrapper for transporting actions
5. **FeatureFlags.cs** - Enum of available feature flags and scopes
6. **FeatureFlagManager.cs** - Manager for checking and setting flags
7. **Actions/GoldChangeAction.cs** - Gold sync via INetAction
8. **Actions/HpChangeAction.cs** - HP sync via INetAction
9. **Actions/MaxHpChangeAction.cs** - MaxHP sync via INetAction

### Current State

- ✅ INetAction contract defined
- ✅ Feature flag system complete
- ✅ NetActionService send/receive infrastructure
- ✅ HP/MaxHP sync via INetAction
- ✅ Gold sync via INetAction (when NetworkedActions enabled)
- ⚠️ NetworkedActions flag defaults to `false` (legacy sync still active)

## Using INetAction

### Creating a New Action

```csharp
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SoulLinkMod;

public struct MyCustomAction : INetAction
{
    // Action data (must be serializable)
    public int SomeValue;
    public string SomeText;

    // Network properties
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Debug;

    // Serialization
    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(SomeValue);
        writer.WriteString(SomeText);
    }

    public void Deserialize(PacketReader reader)
    {
        SomeValue = reader.ReadInt();
        SomeText = reader.ReadString();
    }

    // Execution (called on all clients)
    public void Execute(INetActionContext context)
    {
        // Check feature flags
        if (!FeatureFlagManager.IsEnabled(FeatureFlag.SoulLinkEnabled))
            return;

        // Apply deterministic effects here
        GD.Print($"[MyAction] Player {context.OriginatingPlayerSlot} " +
                 $"executed with value={SomeValue}");

        // Update game state...
    }
}
```

### Sending Actions

```csharp
NetActionService.EnqueueLocalAction(new MyCustomAction
{
    SomeValue = 42,
    SomeText = "Hello"
}, playerSlot);
```

## Using Feature Flags

### Checking Flags

```csharp
if (FeatureFlagManager.IsEnabled(FeatureFlag.SharedHealthPool))
{
    // Apply shared HP logic
}
else
{
    // Use STS2 default behavior
}
```

### Setting Flags

```csharp
// Enable a feature
FeatureFlagManager.SetFlag(FeatureFlag.DebugOverlay, true);

// Disable a feature
FeatureFlagManager.SetFlag(FeatureFlag.VerboseNetworkLogging, false);
```

### Available Flags

| Flag | Default | Scope | Description |
|------|---------|-------|-------------|
| `SoulLinkEnabled` | true | Global | Master enable/disable for all Soul Link features |
| `SharedHealthPool` | true | Run | Shared HP pool mechanic |
| `GoldSharing` | true | Run | Gold sharing mechanics |
| `NetworkedActions` | **false** | Session | INetAction system (disabled by default for testing) |
| `DebugOverlay` | true | Session | Debug UI panel |
| `CombatLog` | true | Session | Combat log panel |
| `RunStatsPanel` | true | Session | Run stats panel |
| `VerboseNetworkLogging` | false | Session | Verbose network logs |

## Migration Path

The feature flag system allows gradual migration from legacy sync to INetAction-based sync:

1. ~~**Phase 1**: Contracts defined, `NetworkedActions` = false, legacy sync active~~
2. ~~**Phase 2**: Implement action send/receive infrastructure, test alongside legacy~~
3. ~~**Phase 3**: Port existing features (gold, HP) to INetAction, feature-flagged~~
4. **Phase 4** (next): Enable `NetworkedActions` by default, deprecate legacy patches
5. **Phase 5**: Remove legacy sync code

## Next Steps

### Remaining Work

- [ ] Console commands for toggling flags at runtime
- [ ] Save/load global flags from mod settings
- [ ] Broadcast run flags at run start
- [ ] Sync flags when new player joins
- [ ] Enable NetworkedActions by default after testing

## Testing
1. Build the mod: `dotnet build`
2. Run FastMP with multiple clients
3. Toggle `NetworkedActions` flag via console
4. Verify deterministic behavior in both modes
5. Compare state checksums across clients

## Design Principles

1. **Deterministic**: Same action inputs → same outputs on all clients
2. **Idempotent**: Safe to execute multiple times (where possible)
3. **Feature-gated**: Check flags before applying effects
4. **Logged**: Include context for debugging desyncs
5. **Serializable**: Use typed PacketWriter/Reader APIs
6. **Ordered**: Execute in FIFO queue order for state consistency

## References

- Existing network messages: `SoulLinkGoldSyncMessage.cs`, `SoulLinkSettingsSyncMessage.cs`
- Existing sync patches: `Patches/GoldSyncPatch.cs`, `Patches/HpSyncPatch.cs`
- STS2 multiplayer docs: See CLAUDE.md "Slay the Spire 2 Multiplayer" section
