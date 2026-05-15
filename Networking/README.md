# Networking Directory

This directory organizes the Soul Link mod's networking code by architectural layer.

## Directory Structure

### `Session/`
**Purpose**: Run-start configuration transfer (long-lived settings sync)

Contains messages sent once at the beginning of a multiplayer run to synchronize host settings with all clients.

**Files**:
- `SoulLinkSettingsSyncMessage.cs` - Broadcasts run settings (split HP/heal, gold mode, etc.) from host to clients

**Status**: Stable, will remain through full VGQ migration

---

### `Wire/`
**Purpose**: Legacy gold wire format (to be deleted when migrated)

Contains the original direct gold synchronization message, bypassing the action queue. Replaced by the MNA pipeline's `GoldChangeAction`.

**Files**:
- `SoulLinkGoldSyncMessage.cs` - Direct gold sync message (pre-INetAction approach)

**Deletion milestone**: Remove after MNA gold sync proven stable and `NetworkedActions` flag enabled by default

---

### `MNA/`
**Purpose**: Mod Net Action pipeline (to be deleted after VGQ proven)

Contains the mod-local `INetAction` contract and infrastructure. This is a transitional architecture that provides deterministic action ordering without coupling to vanilla `GameAction`.

**Files**:
- `INetAction.cs` - Contract for networked state-changing operations
- `NetActionContext.cs` - Execution context for actions (player slot, timestamp, local/remote)
- `NetActionService.cs` - Send/receive dispatcher with re-entrancy protection
- `Actions/` - Concrete action implementations (HP, MaxHP, Gold changes)
- `Messages/` - Network message wrappers for each action type

**Deletion milestone**: Remove after vanilla `GameAction` queue integration (VGQ) is complete and proven deterministic

---

### `VGQ/`
**Purpose**: Vanilla GameAction Queue types (target architecture)

Target architecture for multiplayer synchronization. Will contain custom `GameAction` subclasses that use STS2's native action queue for ordering guarantees.

**Status**: Not yet implemented - folder reserved for future migration

**Migration trigger**: Create ADR documenting queue-first decision, then implement `ToNetAction()`/`ToGameAction()` conversions

---

## Migration Path

1. **Current state**: MNA active behind `NetworkedActions` flag (default: off), Wire legacy active
2. **Next phase**: Enable `NetworkedActions` by default, deprecate Wire
3. **Final phase**: Implement VGQ, prove determinism, delete MNA and Wire

## Related Documentation

- `INETACTION_GUIDE.md` - INetAction usage and patterns
- [ADR 0001: Queue-First Synchronization](../docs/adr/0001-queue-first-synchronization.md) - Queue-first vs dual pipeline decision
