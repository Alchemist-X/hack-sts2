namespace Sts2Sim.Core.Relics;

public sealed class MeatOnTheBone : RelicModel
{
    public const string RelicId = "MEAT_ON_THE_BONE";

    public const string HpThresholdKey = "HpThreshold";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(HpThresholdKey, 50m),
        new HealVar(12m)
    };

    public override void BeforeCombatStart(RunState rs)
    {
        if (WillHealOnCombatFinished())
        {
            Status = RelicStatus.Active;
        }
    }

    public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return;
        }
        if (rs.Combat is not { IsInProgress: true })
        {
            return;
        }
        Status = WillHealOnCombatFinished() ? RelicStatus.Active : RelicStatus.Normal;
    }

    public override void AfterCombatVictoryEarly(RunState rs, CombatRoom? room)
    {
        if (Owner.IsDead)
        {
            return;
        }
        if (!WillHealOnCombatFinished())
        {
            return;
        }
        Status = RelicStatus.Normal;
        var cs = rs.Combat ?? throw new InvalidOperationException("MeatOnTheBone.AfterCombatVictoryEarly: no combat on the run");
        CreatureCmd.Heal(cs, Owner, DynamicVars.Heal.BaseValue);
    }

    private bool WillHealOnCombatFinished()
    {
        var creature = Owner;
        int num = (int)(creature.MaxHp * (DynamicVars[HpThresholdKey].BaseValue / 100m));
        return creature.CurrentHp <= num;
    }
}
