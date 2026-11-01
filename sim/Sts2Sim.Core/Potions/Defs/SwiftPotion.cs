namespace Sts2Sim.Core.Potions;

public sealed class SwiftPotion : PotionModel
{
    public const string PotionId = "SWIFT_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, (PlayerState)target);
    }
}
