using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SoulLinkMod.Patches;

/// <summary>
/// Patches Creature.HealInternal to capture the unclamped heal amount before
/// SetCurrentHpInternal clamps it to MaxHp.
///
/// HealInternal(amount) → SetCurrentHpInternal(CurrentHp + amount) → CurrentHp = Min(...)
///
/// By the time HpSyncPatch sees the CurrentHp setter, the value is already clamped.
/// Storing the unclamped amount here lets ApplyHpDelta scale the true intended heal
/// rather than the smaller clamped delta, preventing under-healing when near full HP.
/// </summary>
[HarmonyPatch(typeof(Creature))]
public static class HealInternalPatch
{
    static MethodBase TargetMethod()
        => AccessTools.Method(typeof(Creature), nameof(Creature.HealInternal));

    static void Prefix(Creature __instance, decimal amount)
    {
        if (SoulLinkMod.ApplyingCanonical) return;
        if (!SoulLinkSession.IsActive) return;
        if (!__instance.IsPlayer) return;
        if (amount <= 0) return;

        // Store the unclamped heal amount. HpSyncPatch will consume it when the
        // CurrentHp setter fires immediately after.
        SoulLinkSession.PendingUnclampedHeal = (int)amount;
    }
}
