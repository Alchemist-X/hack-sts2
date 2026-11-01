using System.Reflection;

namespace Sts2Sim.Core.Monsters;

public static class EncounterMonsterConfig
{
    public static Action<MonsterState> Set(string property, object value) => m => SetExtra(m, property, value);

    public static void SetExtra(MonsterState monster, string property, object value)
    {
        ArgumentNullException.ThrowIfNull(monster);
        var extra = monster.Extra ?? throw new InvalidOperationException($"{monster.MonsterId} has no extra state to carry {property}.");
        var prop = extra.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{monster.MonsterId} extra state {extra.GetType().Name} has no public property {property}.");
        prop.SetValue(extra, value);
    }
}
