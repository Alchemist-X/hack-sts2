namespace Sts2Sim.Core.Powers;

public sealed class TheBombPower : PowerModel
{
    public const string PowerId = "THE_BOMB_POWER";

    public const decimal BaseDamage = 40m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public decimal Damage { get; private set; } = BaseDamage;

    public void SetDamage(decimal damage)
    {
        Damage = damage;
    }

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Amount > 1)
        {
            PowerCmd.Decrement(cs, this);
            return;
        }
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        CreatureCmd.Damage(cs, hittable, new DamageVar(Damage, ValueProp.Unpowered), Owner);
        PowerCmd.Remove(cs, this);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(Damage);
    }
}
