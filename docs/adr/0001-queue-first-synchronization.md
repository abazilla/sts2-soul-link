# ADR 0001: Queue-First Synchronization Architecture

## Status

Accepted

## Context

Soul Link mod requires deterministic state synchronization across multiplayer peers for shared mechanics (HP pool, gold sharing). Initial implementation used direct Harmony patches with custom `INetMessage` broadcasts (the "Wire" approach), which worked for simple cases but exhibited fundamental ordering problems:

### The Dual Timeline Problem

When running two parallel synchronization pipelines (mod messages + vanilla game actions), peers can process events in different orders:

**Scenario: Enemy steals gold during combat**

- **Peer A timeline**:
  1. Vanilla `GoldLostGameAction` executes → gold -= 50
  2. Harmony patch fires → broadcasts `SoulLinkGoldSyncMessage`
  3. Other game actions continue...

- **Peer B timeline**:
  1. Network message arrives first (faster route)
  2. `SoulLinkGoldSyncMessage` handler applies → gold -= 50
  3. Vanilla `GoldLostGameAction` arrives and executes → gold -= 50 again

**Result**: Peer A has correct gold, Peer B double-deducted. State divergence leads to checksum mismatches and desync.

### Root Cause

Vanilla `GameAction` queue and mod network messages are separate ordering domains. Network latency, action queue depth, and message handler timing create race conditions where the same logical event can be processed in different orders on different machines.

### Attempted Solutions

1. **Deduplication dictionaries** - Added timestamp-based dedup to detect and skip double-application. Works for simple cases but:
   - Requires perfect timestamp synchronization
   - Breaks down with multiple effects in a single frame
   - Complex to maintain and debug

2. **Cancellation tokens** - Track pending local actions and cancel network echoes. Better, but:
   - Still doesn't solve out-of-order arrival vs other game actions
   - Adds cognitive overhead to every sync patch
   - Fragile when actions trigger cascading effects

These are band-aids on a fundamental architectural mismatch: **we're maintaining two timelines for the same events**.

## Decision

**Adopt vanilla `GameAction` queue as the single source of truth for multiplayer synchronization (VGQ architecture).**

All Soul Link state mutations will be modeled as custom `GameAction` subclasses that integrate with STS2's native action queue. Network synchronization happens via `ToNetAction()` / `ToGameAction()` conversion (the paired vanilla INetAction type that comes with GameAction).

### Migration Strategy

1. **Phase 1-3 (Complete)**: Build transitional MNA (Mod Net Action) pipeline
   - Prove action-based synchronization works
   - Establish serialization patterns
   - Test ordering guarantees in isolation

2. **Phase 4 (Current)**: Document architectural decision (this ADR)

3. **Phase 5**: Implement VGQ types
   - Create custom `GameAction` subclasses for Soul Link effects
   - Implement `ToNetAction()` / `ToGameAction()` methods
   - Feature-flag for gradual rollout

4. **Phase 6**: Deprecate and remove
   - Enable VGQ by default after proving determinism
   - Delete MNA infrastructure
   - Delete Wire legacy code

### Why VGQ Over MNA Long-Term

MNA proved the viability of action-based sync but remains a parallel pipeline:
- Still requires deduplication logic
- Doesn't integrate with vanilla action ordering (Card effects → Relics → Powers)
- Custom serialization misses vanilla action metadata (source tracking, priority)

VGQ solves this by **using the queue that already orders all game state changes**. If vanilla can synchronize card plays, relic procs, and combat effects deterministically, our shared mechanics should use the same infrastructure.

## Consequences

### Positive

- **Single timeline**: One queue orders all state changes, eliminating race conditions
- **Deterministic ordering**: Vanilla action queue provides battle-tested FIFO guarantees
- **Better debugging**: Action history shows Soul Link effects alongside vanilla effects
- **Reduced complexity**: No deduplication, no cancellation tokens, no timestamp sync
- **Future-proof**: Leverages STS2's multiplayer infrastructure improvements

### Negative

- **Engine coupling**: More dependent on vanilla `GameAction` internal behavior
  - If STS2 changes action queue implementation, we must adapt
  - Harder to mock/test without full game environment
  
- **Implementation complexity**: Custom `GameAction` subclasses are more complex than simple message handlers
  - Requires understanding action lifecycle (ctor → Apply → OnComplete → etc)
  - Serialization must align with vanilla patterns
  
- **Migration cost**: Non-trivial refactor from MNA to VGQ
  - Each sync point (HP, gold, MaxHP) needs a `GameAction` subclass
  - Must coordinate with vanilla actions that trigger same effects

### Risks and Rollback Plan

**Risk**: VGQ integration reveals unforeseen ordering issues or vanilla action limitations

**Mitigation**:
1. **Feature flags**: `UseVGQSync` defaults to false during initial implementation
2. **Parallel operation**: VGQ and MNA run side-by-side during testing phase
3. **Metrics**: Compare checksums and action logs between VGQ/MNA modes
4. **Fast rollback**: If critical bugs surface, disable `UseVGQSync` flag to revert to proven MNA

**Rollback triggers**:
- State divergence in VGQ that doesn't occur in MNA
- Vanilla action side effects we cannot control
- Performance degradation from action queue overhead
- STS2 update breaks `GameAction` contracts

If rollback needed:
- Keep MNA infrastructure until VGQ proven (already planned)
- Document specific VGQ failure modes for future retry
- Consider hybrid approach (VGQ for some effects, MNA for others)

## Related

- **Networking/README.md** - Directory structure documenting MNA → VGQ migration path
- **INETACTION_GUIDE.md** - MNA implementation details and patterns
- **Phase 1-3 commits** - MNA proof of concept (issues sts2-soul-link-79w, sts2-soul-link-610, sts2-soul-link-3z1)

## Notes

This decision prioritizes **correctness over convenience**. Dual timeline problems are notoriously difficult to debug (intermittent, timing-dependent, only visible in multiplayer). Accepting tighter coupling to vanilla action queue is the price for deterministic ordering guarantees.

The MNA phase was not wasted work - it validated that action-based synchronization solves our ordering problems and established serialization patterns we'll reuse in VGQ. Think of MNA as a prototype that proved the approach before committing to engine integration.
