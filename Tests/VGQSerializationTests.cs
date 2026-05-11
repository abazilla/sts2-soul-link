using System;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using SoulLinkMod.VGQ;

namespace SoulLinkMod.Tests;

/// <summary>
/// Manual tests for VGQ action serialization round-trips.
/// Run these via console commands or attach to test runner when available.
/// </summary>
public static class VGQSerializationTests
{
    public static void RunAll()
    {
        GD.Print("[VGQSerializationTests] Starting all serialization round-trip tests...");

        bool allPassed = true;
        allPassed &= TestHpChangeNetAction();
        allPassed &= TestMaxHpChangeNetAction();
        allPassed &= TestGoldChangeNetAction();

        if (allPassed)
        {
            GD.Print("[VGQSerializationTests] ✓ ALL TESTS PASSED");
        }
        else
        {
            GD.PrintErr("[VGQSerializationTests] ✗ SOME TESTS FAILED");
        }
    }

    public static bool TestHpChangeNetAction()
    {
        GD.Print("[VGQSerializationTests] Testing SoulLinkHpChangeNetAction serialization...");

        try
        {
            // Create original action
            var original = new SoulLinkHpChangeNetAction
            {
                DeltaHp = -5,
                PlayerSlot = 1,
                InCombat = true,
                Source = "TestRelic"
            };

            // Serialize
            var writer = new PacketWriter();
            original.Serialize(writer);
            byte[] buffer = writer.Buffer;
            int bitPosition = writer.BitPosition;

            // Deserialize
            var reader = new PacketReader();
            reader.Reset(buffer);
            var deserialized = new SoulLinkHpChangeNetAction();
            deserialized.Deserialize(reader);

            // Verify
            bool passed = true;
            passed &= AssertEqual(original.DeltaHp, deserialized.DeltaHp, "DeltaHp");
            passed &= AssertEqual(original.PlayerSlot, deserialized.PlayerSlot, "PlayerSlot");
            passed &= AssertEqual(original.InCombat, deserialized.InCombat, "InCombat");
            passed &= AssertEqual(original.Source, deserialized.Source, "Source");
            passed &= AssertEqual(bitPosition, reader.BitPosition, "BitPosition (no data left)");

            if (passed)
            {
                GD.Print("[VGQSerializationTests]   ✓ SoulLinkHpChangeNetAction PASSED");
            }
            else
            {
                GD.PrintErr("[VGQSerializationTests]   ✗ SoulLinkHpChangeNetAction FAILED");
            }

            return passed;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VGQSerializationTests]   ✗ Exception: {ex.Message}");
            return false;
        }
    }

    public static bool TestMaxHpChangeNetAction()
    {
        GD.Print("[VGQSerializationTests] Testing SoulLinkMaxHpChangeNetAction serialization...");

        try
        {
            // Create original action
            var original = new SoulLinkMaxHpChangeNetAction
            {
                DeltaMaxHp = 10,
                PlayerSlot = 0,
                InCombat = false,
                Source = "BurningBlood"
            };

            // Serialize
            var writer = new PacketWriter();
            original.Serialize(writer);
            byte[] buffer = writer.Buffer;
            int bitPosition = writer.BitPosition;

            // Deserialize
            var reader = new PacketReader();
            reader.Reset(buffer);
            var deserialized = new SoulLinkMaxHpChangeNetAction();
            deserialized.Deserialize(reader);

            // Verify
            bool passed = true;
            passed &= AssertEqual(original.DeltaMaxHp, deserialized.DeltaMaxHp, "DeltaMaxHp");
            passed &= AssertEqual(original.PlayerSlot, deserialized.PlayerSlot, "PlayerSlot");
            passed &= AssertEqual(original.InCombat, deserialized.InCombat, "InCombat");
            passed &= AssertEqual(original.Source, deserialized.Source, "Source");
            passed &= AssertEqual(bitPosition, reader.BitPosition, "BitPosition (no data left)");

            if (passed)
            {
                GD.Print("[VGQSerializationTests]   ✓ SoulLinkMaxHpChangeNetAction PASSED");
            }
            else
            {
                GD.PrintErr("[VGQSerializationTests]   ✗ SoulLinkMaxHpChangeNetAction FAILED");
            }

            return passed;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VGQSerializationTests]   ✗ Exception: {ex.Message}");
            return false;
        }
    }

    public static bool TestGoldChangeNetAction()
    {
        GD.Print("[VGQSerializationTests] Testing SoulLinkGoldChangeNetAction serialization...");

        try
        {
            // Test SharedPool mode
            var originalShared = new SoulLinkGoldChangeNetAction
            {
                DeltaGold = 50,
                PlayerSlot = 1,
                Mode = GoldSharingMode.SharedPool,
                Source = "ChestReward"
            };

            // Serialize
            var writer = new PacketWriter();
            originalShared.Serialize(writer);
            byte[] buffer = writer.Buffer;
            int bitPosition = writer.BitPosition;

            // Deserialize
            var reader = new PacketReader();
            reader.Reset(buffer);
            var deserialized = new SoulLinkGoldChangeNetAction();
            deserialized.Deserialize(reader);

            // Verify
            bool passed = true;
            passed &= AssertEqual(originalShared.DeltaGold, deserialized.DeltaGold, "DeltaGold");
            passed &= AssertEqual(originalShared.PlayerSlot, deserialized.PlayerSlot, "PlayerSlot");
            passed &= AssertEqual(originalShared.Mode, deserialized.Mode, "Mode");
            passed &= AssertEqual(originalShared.Source, deserialized.Source, "Source");
            passed &= AssertEqual(bitPosition, reader.BitPosition, "BitPosition (no data left)");

            // Test SplitByPlayer mode
            var originalSplit = new SoulLinkGoldChangeNetAction
            {
                DeltaGold = 25,
                PlayerSlot = 0,
                Mode = GoldSharingMode.SplitByPlayer,
                Source = null
            };

            writer.Reset();
            originalSplit.Serialize(writer);
            buffer = writer.Buffer;
            bitPosition = writer.BitPosition;

            reader.Reset(buffer);
            var deserializedSplit = new SoulLinkGoldChangeNetAction();
            deserializedSplit.Deserialize(reader);

            passed &= AssertEqual(originalSplit.DeltaGold, deserializedSplit.DeltaGold, "DeltaGold (split)");
            passed &= AssertEqual(originalSplit.PlayerSlot, deserializedSplit.PlayerSlot, "PlayerSlot (split)");
            passed &= AssertEqual(originalSplit.Mode, deserializedSplit.Mode, "Mode (split)");
            passed &= AssertEqual(originalSplit.Source, deserializedSplit.Source, "Source (split, null)");
            passed &= AssertEqual(bitPosition, reader.BitPosition, "BitPosition (no data left, split)");

            if (passed)
            {
                GD.Print("[VGQSerializationTests]   ✓ SoulLinkGoldChangeNetAction PASSED");
            }
            else
            {
                GD.PrintErr("[VGQSerializationTests]   ✗ SoulLinkGoldChangeNetAction FAILED");
            }

            return passed;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[VGQSerializationTests]   ✗ Exception: {ex.Message}");
            return false;
        }
    }

    private static bool AssertEqual<T>(T expected, T actual, string fieldName)
    {
        bool equal = expected == null ? actual == null : expected.Equals(actual);
        if (!equal)
        {
            GD.PrintErr($"[VGQSerializationTests]     MISMATCH {fieldName}: expected={expected}, actual={actual}");
        }
        return equal;
    }
}
