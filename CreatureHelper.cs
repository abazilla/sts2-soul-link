using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SoulLinkMod;

/// <summary>
/// Reflection-based helpers for writing to Creature's private HP setters.
/// Direct assignment (creature.CurrentHp = x) fails to compile because the
/// setters are private. Harmony patches can intercept via IL, but calling
/// them from regular C# code requires reflection.
/// </summary>
internal static class CreatureHelper
{
    private static readonly MethodInfo _setCurrentHp =
        AccessTools.PropertySetter(typeof(Creature), nameof(Creature.CurrentHp));

    private static readonly MethodInfo _setMaxHp =
        AccessTools.PropertySetter(typeof(Creature), nameof(Creature.MaxHp));

    private static readonly MethodInfo _setBlock =
        AccessTools.PropertySetter(typeof(Creature), nameof(Creature.Block));

    public static void SetCurrentHp(this Creature c, int value) =>
        _setCurrentHp.Invoke(c, new object[] { value });

    public static void SetMaxHp(this Creature c, int value) =>
        _setMaxHp.Invoke(c, new object[] { value });

    public static void SetBlock(this Creature c, int value) =>
        _setBlock.Invoke(c, new object[] { value });
}
