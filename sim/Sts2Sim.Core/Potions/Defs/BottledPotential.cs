namespace Sts2Sim.Core.Potions;

public sealed class BottledPotential : PotionModel
{
    public const string PotionId = "BOTTLED_POTENTIAL";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(5)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        CardPileCmd.Add(cs, player.Hand.Cards.ToList(), PileType.Draw);
        CardPileCmd.Shuffle(cs, player);
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, player);
    }
}
