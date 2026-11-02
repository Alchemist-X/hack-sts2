using System.Reflection;

namespace Sts2Sim.Core.Monsters;

public static class EncounterMonsterConfig
{
    public static Action<MonsterState> Set(string property, object value) => m => SetExtra(m, property, value);

    public static void SetExtra(MonsterState monster, string property, object value) => Set(monster, property, value);

    public static void Set(MonsterState monster, string member, object value)
    {
        ArgumentNullException.ThrowIfNull(monster);
        var extra = monster.Extra ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no extra state to receive {member}");
        var type = extra.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
        var property = type.GetProperty(member, flags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(extra, value);
            return;
        }
        var field = type.GetField(member, flags) ?? type.GetField("_" + member, flags);
        if (field != null)
        {
            field.SetValue(extra, value);
            return;
        }
        throw new InvalidOperationException($"Monster {monster.MonsterId} extra state {type.Name} has no settable member {member}");
    }
}
