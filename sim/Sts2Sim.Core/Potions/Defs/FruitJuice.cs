namespace Sts2Sim.Core.Potions;

public sealed class FruitJuice : PotionModel
{
    public const string PotionId = "FRUIT_JUICE";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AnyPlayer;

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(5m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.GainMaxHp(cs, target, DynamicVars.MaxHp.BaseValue);
    }
}
