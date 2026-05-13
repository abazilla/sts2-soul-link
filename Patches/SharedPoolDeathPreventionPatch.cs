using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLinkMod.Patches;

/// <summary>
/// SharedPool death prevention. Intercepts CreatureCmd.KillWithoutCheckingWinCondition
/// before vanilla zeroes the creature's HP and runs the pool-wide preventer scan.
///
/// Why this hook (and not Hook.ShouldDie postfix): vanilla's ShouldDie iteration
/// works fine in single-player, but in MP a remote teammate's Fairy/Lizard owner has
/// no CombatState set on its creature on this client, which makes the existing
/// FairyInABottleSoulLinkPatch/LizardTailSoulLinkPatch teammate-guard bail out.
/// The kill-path prefix sidesteps that entirely: when we have a preventer anywhere
/// in the run state, we heal the shared pool, mirror to every creature, and skip
/// the vanilla kill body.
///
/// Gated on HpMode.SharedPool + SharedLoseHp. Vanilla mode still uses the
/// per-creature ShouldDie postfix patches.
/// </summary>
[HarmonyPatch]
public static class SharedPoolDeathPreventionPatch
{
    static MethodBase TargetMethod()
    {
        var m = AccessTools.Method("MegaCrit.Sts2.Core.Commands.CreatureCmd:KillWithoutCheckingWinCondition");
        GD.Print($"[SoulLink][DeathPrev] TargetMethod resolved={(m != null ? m.ToString() : "NULL")}");
        return m;
    }

    static bool Prefix(Creature creature, bool force, ref Task __result)
    {
        var rs = SoulLinkSession.ActiveRunSettings;
        GD.Print($"[SoulLink][DeathPrev] Prefix ENTRY creature={creature?.Name} isPlayer={creature?.IsPlayer} force={force} IsActive={SoulLinkSession.IsActive} HpMode={rs.HpMode} SharedLoseHp={rs.SharedLoseHp}");
        if (force) { GD.Print("[SoulLink][DeathPrev] bail: force"); return true; }
        if (creature == null || !creature.IsPlayer) { GD.Print("[SoulLink][DeathPrev] bail: not player"); return true; }
        if (!SoulLinkSession.IsActive) { GD.Print("[SoulLink][DeathPrev] bail: !IsActive"); return true; }
        if (SoulLinkSession.ActiveRunSettings.HpMode != HpMode.SharedPool) { GD.Print("[SoulLink][DeathPrev] bail: HpMode!=SharedPool"); return true; }
        // SharedLoseHp gate intentionally omitted — pool-wide revive is intrinsic to
        // shared-pool mode and shouldn't depend on the lose-HP-teammate-trigger flag.

        var runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null) { GD.Print("[SoulLink][DeathPrev] bail: runState null"); return true; }

        int dyingSlot = -1;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            GD.Print($"[SoulLink][DeathPrev]   compare slot={i} player.Creature={runState.Players[i].Creature?.Name} match={runState.Players[i].Creature == creature}");
            if (runState.Players[i].Creature == creature)
            {
                dyingSlot = i;
                break;
            }
        }
        if (dyingSlot < 0) { GD.Print("[SoulLink][DeathPrev] bail: dyingSlot<0 (creature not in Players)"); return true; }

        GD.Print($"[SoulLink][DeathPrev] Kill prefix slot={dyingSlot} poolHp={SoulLinkSession.CurrentHp}");

        // If an earlier creature in the same kill batch already triggered prevention
        // and healed the pool, just mirror that value onto this creature and skip
        // the vanilla kill body. Avoids double-consume.
        if (SoulLinkSession.CurrentHp > 0)
        {
            SoulLinkMod.ApplyingCanonical = true;
            try { creature.SetCurrentHp(SoulLinkSession.CurrentHp); }
            finally { SoulLinkMod.ApplyingCanonical = false; }
            __result = Task.CompletedTask;
            return false;
        }

        var (heal, label) = SoulLinkDeathPrevention.TryConsumePreventer(runState, dyingSlot);
        if (heal <= 0)
        {
            GD.Print($"[SoulLink][DeathPrev] no preventer — letting vanilla kill proceed");
            return true;
        }

        int healed = Math.Min(SoulLinkSession.MaxHp, heal);
        SoulLinkSession.ReviveCanonicalHp(healed, dyingSlot, label);

        SoulLinkMod.ApplyingCanonical = true;
        try
        {
            foreach (var p in runState.Players)
                p.Creature.SetCurrentHp(healed);
        }
        finally { SoulLinkMod.ApplyingCanonical = false; }

        GD.Print($"[SoulLink][DeathPrev] revived pool to {healed} — skipping vanilla kill");
        __result = Task.CompletedTask;
        return false;
    }
}
