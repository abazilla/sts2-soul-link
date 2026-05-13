using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.Patches;

/// <summary>
/// SharedPool death-prevention scan. Walks every player's inventory in
/// dying-slot-first order looking for a Fairy in a Bottle (consumed) or
/// unused Lizard Tail (marked used). Returns the heal amount in HP (already
/// rounded), or 0 if nothing found.
///
/// Why this exists: vanilla Hook.ShouldDie only iterates locally-owned
/// hook listeners on each client. In MP shared-pool runs, a teammate's
/// Fairy/Lizard never sees a remote slot's death event. Centralising the
/// check here makes it pool-wide, deterministic per slot order, and
/// independent of who took the killing blow.
///
/// Determinism: every peer's runState.Players list is replicated in slot
/// order; potion slots and relic order are deterministic; logic is pure.
/// So each peer picks the same preventer and mutates the same state
/// without needing a separate broadcast action.
/// </summary>
public static class SoulLinkDeathPrevention
{
    /// <summary>
    /// Try to consume one death-preventer from the shared inventory pool.
    /// Returns heal amount (HP) and a human-readable source label on success.
    /// Heal is 0 if nothing found.
    /// </summary>
    public static (int heal, string label) TryConsumePreventer(RunState runState, int dyingSlot)
    {
        if (runState == null || runState.Players.Count == 0)
        {
            GD.Print($"[SoulLink][DeathPrev] runState empty");
            return (0, "");
        }

        int playerCount = runState.Players.Count;
        GD.Print($"[SoulLink][DeathPrev] scan dyingSlot={dyingSlot} playerCount={playerCount}");

        for (int offset = 0; offset < playerCount; offset++)
        {
            int slot = (dyingSlot + offset) % playerCount;
            var player = runState.Players[slot];
            if (player == null)
            {
                GD.Print($"[SoulLink][DeathPrev]   slot={slot} player=null");
                continue;
            }

            int potionCount = 0;
            foreach (var p in player.PotionSlots) if (p != null) potionCount++;
            GD.Print($"[SoulLink][DeathPrev]   slot={slot} netId={player.NetId} potions={potionCount} relics={player.Relics.Count}");

            for (int i = 0; i < player.PotionSlots.Count; i++)
            {
                var p = player.PotionSlots[i];
                if (p != null) GD.Print($"[SoulLink][DeathPrev]     potion[{i}]={p.GetType().Name}");
            }
            foreach (var r in player.Relics)
            {
                string used = r is LizardTail lt ? $" wasUsed={lt.WasUsed}" : "";
                GD.Print($"[SoulLink][DeathPrev]     relic={r.GetType().Name}{used}");
            }

            // Fairy first (vanilla also prefers ShouldDie over ShouldDieLate).
            for (int i = 0; i < player.PotionSlots.Count; i++)
            {
                if (player.PotionSlots[i] is FairyInABottle fairy)
                {
                    int heal = Math.Max(1, (int)Math.Round(SoulLinkSession.MaxHp * 0.30));
                    player.DiscardPotionInternal(fairy);
                    string label = $"Player healed {heal} health via Fairy in a Bottle";
                    GD.Print($"[SoulLink][DeathPrev] consumed FAIRY_IN_A_BOTTLE from slot={slot} (dying={dyingSlot}) heal={heal}");
                    return (heal, label);
                }
            }

            foreach (var relic in player.Relics)
            {
                if (relic is LizardTail lizard && !lizard.WasUsed)
                {
                    int heal = Math.Max(1, (int)Math.Round(SoulLinkSession.MaxHp * 0.50));
                    lizard.WasUsed = true;
                    string label = $"Player healed {heal} health via Lizard Tail";
                    GD.Print($"[SoulLink][DeathPrev] consumed LIZARD_TAIL from slot={slot} (dying={dyingSlot}) heal={heal}");
                    return (heal, label);
                }
            }
        }

        GD.Print($"[SoulLink][DeathPrev] no preventer found");
        return (0, "");
    }
}
