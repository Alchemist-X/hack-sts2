namespace Sts2Sim.Core.Relics;

public sealed class ArcaneScroll : RelicModel
{
    public const string RelicId = "ARCANE_SCROLL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        var options = new CardCreationOptions(new[] { CardPools.ForCharacter(Owner.CharacterId) }, CardCreationSource.Other, CardRarityOddsType.Uniform, c => c.Rarity == CardRarity.Rare)
            .WithFlags(CardCreationFlags.NoUpgradeRoll);
        var val2 = CardFactory.CreateForReward(rs, Owner, DynamicVars.Cards.IntValue, options).Select(r => r.Card).ToList();
        if (val2.Count > 0)
        {
            CardPileCmd.Add(rs, val2[0], PileType.Deck);
        }
    }
}
