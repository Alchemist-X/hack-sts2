namespace Sts2Sim.Core.Relics;

public sealed class RedSkull : RelicModel
{
    public const string RelicId = "RED_SKULL";

    public const string HpThresholdKey = "HpThreshold";

    private bool _strengthApplied;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(HpThresholdKey, 50m),
        new PowerVar<StrengthPower>(3m)
    };

    private bool StrengthApplied
    {
        get => _strengthApplied;
        set => _strengthApplied = value;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".AfterRoomEntered: combat room without an attached combat");
        ModifyStrengthIfNecessary(cs);
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        StrengthApplied = false;
        Status = RelicStatus.Normal;
    }

    public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress)
        {
            ModifyStrengthIfNecessary(cs);
        }
    }

    private void ModifyStrengthIfNecessary(CombatState cs)
    {
        var creature = Owner;
        bool flag = (decimal)creature.CurrentHp > (decimal)creature.MaxHp * (DynamicVars[HpThresholdKey].BaseValue / 100m);
        Status = !flag ? RelicStatus.Active : RelicStatus.Normal;
        decimal baseValue = DynamicVars.Strength.BaseValue;
        if (flag && StrengthApplied)
        {
            PowerCmd.Apply<StrengthPower>(cs, creature, -baseValue, creature, null);
            StrengthApplied = false;
        }
        else if (!flag && !StrengthApplied)
        {
            PowerCmd.Apply<StrengthPower>(cs, creature, baseValue, creature, null);
            StrengthApplied = true;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_strengthApplied);
    }
}
