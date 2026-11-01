namespace Sts2Sim.Core.Potions;

public sealed class StarPotion : PotionModel
{
    public const string PotionId = "STAR_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(3)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PlayerCmd.GainStars(cs, DynamicVars.Stars.BaseValue, (PlayerState)target);
    }
}
