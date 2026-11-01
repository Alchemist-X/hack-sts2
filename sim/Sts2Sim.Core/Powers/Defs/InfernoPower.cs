namespace Sts2Sim.Core.Powers;

public sealed class InfernoPower : PowerModel
{
    public const string PowerId = "INFERNO_POWER";

    public const decimal BaseSelfDamage = 0m;

    public const ValueProp SelfDamageProps = ValueProp.Unblockable | ValueProp.Unpowered;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal SelfDamage { get; private set; } = BaseSelfDamage;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        CreatureCmd.Damage(cs, Owner, SelfDamage, SelfDamageProps, Owner);
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat!;
        if (ReferenceEquals(target, Owner) && result.UnblockedDamage > 0 && cs.CurrentSide == Owner.Side)
        {
            CreatureCmd.Damage(cs, HittableEnemies(cs), Amount, ValueProp.Unpowered, Owner);
        }
    }

    public void IncrementSelfDamage()
    {
        SelfDamage += 1m;
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(SelfDamage);
    }
}
