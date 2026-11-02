namespace Sts2Sim.Core.Relics;

public sealed class BeatingRemnant : RelicModel
{
    public const string RelicId = "BEATING_REMNANT";

    public const string MaxHpLossKey = "MaxHpLoss";

    private decimal _damageReceivedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    private decimal DamageReceivedThisTurn
    {
        get => _damageReceivedThisTurn;
        set => _damageReceivedThisTurn = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(MaxHpLossKey, 20m)
    };

    public override decimal ModifyHpLostAfterOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs == null || !cs.IsInProgress)
        {
            return amount;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        return Math.Min(amount, DynamicVars[MaxHpLossKey].BaseValue - DamageReceivedThisTurn);
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs == null || !cs.IsInProgress)
        {
            return;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        DamageReceivedThisTurn += result.UnblockedDamage;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        DamageReceivedThisTurn = 0m;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(_damageReceivedThisTurn);
    }
}
