namespace Sts2Sim.Core.Relics;

public sealed class BlessedAntler : RelicModel
{
    public const string RelicId = "BLESSED_ANTLER";

    public const string DazedId = "DAZED";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new CardsVar(3)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        var cards = new List<CardInstance>();
        for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
        {
            cards.Add(cs.CreateCard(DazedId, Owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Draw, Owner, CardPilePosition.Random);
    }
}
