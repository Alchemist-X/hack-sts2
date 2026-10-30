namespace Sts2Sim.Core.Monsters;

public abstract class AbstractIntent
{
    public abstract IntentType IntentType { get; }
}

public abstract class AttackIntent : AbstractIntent
{
    protected AttackIntent(int damage)
    {
        BaseDamage = damage;
        DamageCalc = (_, _) => damage;
    }

    protected AttackIntent(Func<CombatState, MonsterState, decimal> damageCalc)
    {
        DamageCalc = damageCalc;
    }

    public override IntentType IntentType => IntentType.Attack;

    public int BaseDamage { get; }

    public Func<CombatState, MonsterState, decimal>? DamageCalc { get; }

    public decimal GetBaseDamage(CombatState cs, MonsterState owner)
    {
        var calc = DamageCalc ?? throw new InvalidOperationException("Attack intent has no damage calculation");
        return calc(cs, owner);
    }

    public abstract int GetRepeats(CombatState cs, MonsterState owner);

    public abstract int GetTotalDamage(CombatState cs, IReadOnlyList<CreatureState> targets, MonsterState owner);

    public int GetSingleDamage(CombatState cs, IReadOnlyList<CreatureState> targets, MonsterState owner)
    {
        decimal num = GetBaseDamage(cs, owner);
        if (CombatStatus.IsAttached(cs, owner))
        {
            num = Hook.ModifyDamage(cs, cs.Player, owner, GetBaseDamage(cs, owner), ValueProp.Move, null, null, ModifyDamageHookType.All);
        }
        return Math.Max(0, (int)num);
    }
}

public class SingleAttackIntent : AttackIntent
{
    public SingleAttackIntent(int damage) : base(damage)
    {
    }

    public SingleAttackIntent(Func<CombatState, MonsterState, decimal> damageCalc) : base(damageCalc)
    {
    }

    public override int GetRepeats(CombatState cs, MonsterState owner) => 1;

    public override int GetTotalDamage(CombatState cs, IReadOnlyList<CreatureState> targets, MonsterState owner)
        => GetSingleDamage(cs, targets, owner);
}

public sealed class DeathBlowIntent : SingleAttackIntent
{
    public DeathBlowIntent(Func<CombatState, MonsterState, decimal> damageCalc) : base(damageCalc)
    {
    }

    public override IntentType IntentType => IntentType.DeathBlow;
}

public sealed class MultiAttackIntent : AttackIntent
{
    private readonly int _repeat;
    private readonly Func<CombatState, MonsterState, int>? _repeatCalc;

    public MultiAttackIntent(int damage, int repeat) : base(damage)
    {
        _repeat = repeat;
    }

    public MultiAttackIntent(int damage, Func<CombatState, MonsterState, int> repeatCalc) : base(damage)
    {
        _repeatCalc = repeatCalc;
    }

    public override int GetRepeats(CombatState cs, MonsterState owner) => _repeatCalc?.Invoke(cs, owner) ?? _repeat;

    public override int GetTotalDamage(CombatState cs, IReadOnlyList<CreatureState> targets, MonsterState owner)
        => GetSingleDamage(cs, targets, owner) * GetRepeats(cs, owner);
}

public sealed class SimpleIntent : AbstractIntent
{
    public SimpleIntent(IntentType type)
    {
        IntentType = type;
    }

    public override IntentType IntentType { get; }
}

public sealed class DebuffIntent : AbstractIntent
{
    public DebuffIntent(bool strong = false)
    {
        Strong = strong;
    }

    public bool Strong { get; }

    public override IntentType IntentType => Strong ? IntentType.DebuffStrong : IntentType.Debuff;
}

public sealed class StatusIntent : AbstractIntent
{
    public StatusIntent(int count)
    {
        CardCount = count;
    }

    public int CardCount { get; }

    public override IntentType IntentType => IntentType.StatusCard;
}

public static class Intents
{
    public static readonly SimpleIntent Defend = new(IntentType.Defend);
    public static readonly SimpleIntent Buff = new(IntentType.Buff);
    public static readonly SimpleIntent Unknown = new(IntentType.Unknown);
    public static readonly SimpleIntent Stun = new(IntentType.Stun);
    public static readonly SimpleIntent Sleep = new(IntentType.Sleep);
    public static readonly SimpleIntent Heal = new(IntentType.Heal);
    public static readonly SimpleIntent Escape = new(IntentType.Escape);
    public static readonly SimpleIntent Summon = new(IntentType.Summon);
    public static readonly SimpleIntent Hidden = new(IntentType.Hidden);
    public static readonly SimpleIntent CardDebuff = new(IntentType.CardDebuff);
}
