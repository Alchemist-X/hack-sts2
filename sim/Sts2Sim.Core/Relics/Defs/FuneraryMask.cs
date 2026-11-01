namespace Sts2Sim.Core.Relics;

public sealed class FuneraryMask : RelicModel
{
    public const string RelicId = "FUNERARY_MASK";

    public const string SoulCardId = "SOUL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        for (int i = 0; i < DynamicVars.Cards.BaseValue; i++)
        {
            var card = cs.CreateCard(SoulCardId, Owner);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Draw, Owner, CardPilePosition.Random);
        }
    }
}
