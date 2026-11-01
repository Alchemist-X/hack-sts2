using System.Reflection;

namespace Sts2Sim.Core.Powers;

public static class MonsterPowerCoupling
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public static bool Is(CreatureState creature, string monsterId, out MonsterState monster)
    {
        if (creature is MonsterState m && m.MonsterId == monsterId)
        {
            monster = m;
            return true;
        }
        monster = null!;
        return false;
    }

    public static MonsterState Require(CreatureState creature, string monsterId, string site)
    {
        if (creature is MonsterState m && m.MonsterId == monsterId)
        {
            return m;
        }
        throw new InvalidCastException($"{site}: owner is not {monsterId}");
    }

    public static void Call(CombatState cs, MonsterState monster, string member, string site)
    {
        var method = FindMethod(monster, member, site);
        var parameters = method.GetParameters();
        object? target = method.IsStatic ? null : monster.Def;
        object?[] args = parameters.Length == 2
            ? new object?[] { cs, monster }
            : new object?[] { cs, monster, Array.Empty<CreatureState>() };
        try
        {
            method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    public static MovePerform Move(MonsterState monster, string member, string site)
    {
        var method = FindMethod(monster, member, site);
        if (method.GetParameters().Length != 3)
        {
            throw new NotImplementedException($"W3 gap: {site} needs {monster.MonsterId}.{member} as a MovePerform (CombatState, MonsterState, IReadOnlyList<CreatureState>)");
        }
        return method.IsStatic
            ? (MovePerform)Delegate.CreateDelegate(typeof(MovePerform), method)
            : (MovePerform)Delegate.CreateDelegate(typeof(MovePerform), monster.Def, method);
    }

    public static void SetFlag(MonsterState monster, string property, bool value, string site)
    {
        object? holder = monster.Extra;
        var prop = holder?.GetType().GetProperty(property, Flags);
        if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(bool))
        {
            throw new NotImplementedException($"W3 gap: {site} sets {monster.MonsterId} extra-state bool '{property}', which is not ported");
        }
        prop.SetValue(holder, value);
    }

    public static MoveStateDef MachineMove(MonsterState monster, string stateId)
        => (MoveStateDef)monster.Ai.Machine.Get(stateId);

    private static MethodInfo FindMethod(MonsterState monster, string member, string site)
    {
        var methods = monster.Def.GetType().GetMethods(Flags);
        for (int i = 0; i < methods.Length; i++)
        {
            var m = methods[i];
            if (m.Name != member)
            {
                continue;
            }
            var p = m.GetParameters();
            if (p.Length >= 2 && p.Length <= 3 && p[0].ParameterType == typeof(CombatState) && p[1].ParameterType.IsAssignableFrom(typeof(MonsterState))
                && (p.Length == 2 || p[2].ParameterType == typeof(IReadOnlyList<CreatureState>)))
            {
                return m;
            }
        }
        throw new NotImplementedException($"W3 gap: {site} calls {monster.MonsterId}.{member}(CombatState, MonsterState[, IReadOnlyList<CreatureState>]), which is not ported");
    }
}
