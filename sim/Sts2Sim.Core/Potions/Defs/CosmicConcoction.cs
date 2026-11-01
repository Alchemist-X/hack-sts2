namespace Sts2Sim.Core.Potions;

public sealed class CosmicConcoction : PotionModel
{
    public const string PotionId = "COSMIC_CONCOCTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

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
        var player = (PlayerState)target;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        foreach (var current in CardFactory.GetDistinctForCombat(cs, player, options, DynamicVars.Cards.IntValue, rs.Rng.CombatCardGeneration))
        {
            CardCmd.Upgrade(cs, current);
            CardPileCmd.AddGeneratedCardToCombat(cs, current, PileType.Hand, Owner);
        }
    }
}
