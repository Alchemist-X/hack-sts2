namespace Sts2Sim.Core.Potions;

public sealed class DistilledChaos : PotionModel
{
    public const string PotionId = "DISTILLED_CHAOS";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(3)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CardPileCmd.AutoPlayFromDrawPile(cs, (PlayerState)target, DynamicVars.Repeat.IntValue, CardPilePosition.Top, forceExhaust: false);
    }
}
