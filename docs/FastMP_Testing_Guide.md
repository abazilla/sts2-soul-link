# FastMP Testing Guide

This guide documents manual testing procedures for verifying VGQ (Vanilla GameAction Queue) multiplayer synchronization in SoulLink mod.

## Prerequisites

- STS2 with FastMP mode enabled
- SoulLink mod installed
- `UseVGQSync` feature flag enabled

## Quick Start

### Running VGQ Serialization Tests

The VGQ serialization round-trip tests can be run programmatically:

```csharp
SoulLinkMod.Tests.VGQSerializationTests.RunAll();
```

These tests verify that:
- HP change actions serialize/deserialize correctly
- MaxHP change actions serialize/deserialize correctly  
- Gold change actions serialize/deserialize correctly (both modes)

## Manual Test Scenarios

### 1. Combat-End Scenario (Timing-Sensitive Heals)

**Purpose**: Verify that combat-end heals (e.g., Burning Blood relic) apply correctly on both host and client.

**Setup**:
1. Start FastMP session with 2 players
2. Player 1 (host) has Burning Blood relic
3. Enable `SharedHealthPool` and `UseVGQSync` feature flags

**Steps**:
1. Enter combat
2. Take damage during combat
3. End combat (win or flee)
4. Observe Burning Blood heal trigger

**Verify**:
- Host and client HP match after heal
- No "divergence" logs in console
- HP delta logged correctly in debug overlay
- No double-application of heal

**Expected Behavior**:
- Both players see same HP value
- Heal appears in combat log once
- UI panels update consistently

---

### 2. Gold Gain Blocked Scenario

**Purpose**: Verify that hooks can block gold gains correctly in multiplayer.

**Setup**:
1. Start FastMP session with 2 players
2. Install a mod/relic that blocks gold gains via `ShouldGainGold` hook
3. Enable `GoldSharing` and `UseVGQSync` feature flags

**Steps**:
1. Trigger a gold reward (chest, combat win, etc.)
2. Verify hook blocks the gain

**Verify**:
- Gold remains unchanged on both host and client
- "blocked" flag propagates correctly
- No gold desync between players
- Combat log shows blocked gain event

**Expected Behavior**:
- Gold stays at same value on both clients
- No error logs about gold mismatch

---

### 3. Chained Rewards Scenario

**Purpose**: Verify that multiple relic triggers from a single event don't cause double-deltas or desyncs.

**Setup**:
1. Start FastMP session with 2 players
2. Equip multiple relics that trigger on same event (e.g., combat win relics)
3. Enable `SharedHealthPool` and `UseVGQSync` feature flags

**Steps**:
1. Trigger the chained event (e.g., win combat)
2. Observe multiple relic effects apply

**Verify**:
- All effects apply once (not duplicated)
- Host and client HP/gold match after all effects
- Event ordering is deterministic
- Checksum matches at snapshot points

**Expected Behavior**:
- Effects appear in predictable order
- No race conditions or missing effects
- Combat log shows all effects

---

### 4. Room Transition Scenario

**Purpose**: Verify that checksums validate correctly during room transitions.

**Setup**:
1. Start FastMP session with 2 players
2. Enable all Soul Link features
3. Enable `UseVGQSync` feature flag

**Steps**:
1. Complete a combat or event
2. Transition to next room
3. Observe checksum validation in logs

**Verify**:
- No checksum mismatch errors
- HP/MaxHP/Gold match on both clients
- No silent desync that appears later

**Expected Behavior**:
- "Checksum passed" log entries
- No divergence warnings

---

### 5. VGQ Action Serialization Round-Trips

**Status**: ✓ Automated tests implemented

See `Tests/VGQSerializationTests.cs` for automated test suite.

**Run Tests**:
```csharp
SoulLinkMod.Tests.VGQSerializationTests.RunAll();
```

**Coverage**:
- `SoulLinkHpChangeNetAction` serialization
- `SoulLinkMaxHpChangeNetAction` serialization
- `SoulLinkGoldChangeNetAction` serialization (both SharedPool and SplitByPlayer modes)

---

## Debugging Tips

### Enable Verbose Logging

Set `UseVGQSync` feature flag and watch for these log patterns:

```
[SoulLink][VGQ] Apply: slot=X delta=Y poolBefore=Z
[SoulLink][VGQ] Apply MaxHP: slot=X delta=Y poolBefore=Z
[SoulLink][VGQ] Apply Gold: slot=X delta=Y mode=SharedPool
```

### Check for Divergence

Look for these error patterns:

```
[SoulLink][VGQ] Apply failed: no run state
[SoulLink][VGQ] Apply failed: invalid player slot
[SoulLink] HP/MaxHP/Gold MISMATCH
```

### Verify Queue Ordering

Enable action queue debugging to see execution order:
- Check that VGQ actions execute in FIFO order
- Verify no re-entrancy issues
- Confirm ActionType matches context (Combat vs NonCombat)

---

## CI Integration (Future)

To automate these tests in CI:

1. Set up headless FastMP test harness
2. Script the manual test scenarios above
3. Add state snapshot comparisons (host vs client)
4. Integrate with existing CI pipeline

**Blockers for automation**:
- FastMP headless mode support
- Programmatic client spawn/control
- State inspection API for automated verification

---

## Issue Resolution

This testing guide addresses issue **sts2-soul-link-47i**: FastMP regression test suite.

**Completed**:
- ✓ VGQ action serialization round-trip tests (automated)
- ✓ Manual testing procedures documented

**Deferred** (requires FastMP test harness implementation):
- Automated combat-end scenario test
- Automated gold-blocked scenario test
- Automated chained rewards scenario test
- Automated room transition scenario test

Manual testing procedures above provide immediate validation capability while automated harness is developed.
