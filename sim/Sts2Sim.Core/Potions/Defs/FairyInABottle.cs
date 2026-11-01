namespace Sts2Sim.Core.Potions;

public sealed class FairyInABottle : PotionModel
{
    public const string PotionId = "FAIRY_IN_A_BOTTLE";

    public const decimal HealMaxHpFraction = 0.3m;

    public const decimal MinimumHeal = 1m;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.Automatic;

    public override TargetType TargetType => TargetType.Self;

    public override bool CanBeGeneratedInCombat => false;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.Heal(cs, target, Math.Max((decimal)target.MaxHp * HealMaxHpFraction, MinimumHeal));
    }

    public override bool ShouldDie(RunState rs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        return false;
    }

    public override void AfterPreventingDeath(RunState rs, CreatureState creature)
    {
        using var context = rs.Choice.PushContext(ChoiceContextKind.Throwing);
        OnUseWrapper(rs, creature);
    }
}
