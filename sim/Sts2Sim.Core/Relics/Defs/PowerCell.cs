namespace Sts2Sim.Core.Relics;

public sealed class PowerCell : RelicModel
{
    public const string RelicId = "POWER_CELL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Contains(Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        var cards = Owner.DrawPile.Cards
            .Where(c => !c.EnergyCost.CostsX && c.EnergyCost.GetWithModifiers(CostModifiers.Local) == 0)
            .ToList()
            .StableShuffle(cs.Rng.CombatCardSelection, CardPileCmd.CompareForShuffle)
            .Take(DynamicVars.Cards.IntValue)
            .ToList();
        CardPileCmd.Add(cs, cards, PileType.Hand);
    }
}
