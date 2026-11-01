namespace Sts2Sim.Core.Potions;

public sealed class SneckoOil : PotionModel
{
    public const string PotionId = "SNECKO_OIL";

    public const int EnergyCostRange = 4;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(7)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, player);
        foreach (var current in player.Hand.Cards.Where(static c => !c.EnergyCost.CostsX))
        {
            if (current.EnergyCost.GetWithModifiers(CostModifiers.None) >= 0)
            {
                current.EnergyCost.SetThisTurnOrUntilPlayed(NextEnergyCost(rs));
            }
        }
    }

    private static int NextEnergyCost(RunState rs)
    {
        return rs.Rng.CombatEnergyCosts.NextInt(EnergyCostRange);
    }
}
