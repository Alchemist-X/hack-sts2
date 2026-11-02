namespace Sts2Sim.Core.Relics;

public sealed class RadiantPearl : RelicModel
{
    public const string RelicId = "RADIANT_PEARL";

    public const string LuminesceCardId = "LUMINESCE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        var cards = new List<CardInstance>();
        for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
        {
            cards.Add(cs.CreateCard(LuminesceCardId, Owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, Owner);
    }
}
