namespace Sts2Sim.Core.Relics;

public sealed class LargeCapsule : RelicModel
{
    public const string RelicId = "LARGE_CAPSULE";

    public const string RelicsKey = "Relics";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new IntVar(RelicsKey, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        for (int i = 0; i < DynamicVars[RelicsKey].IntValue; i++)
        {
            var relic = RelicFactory.PullNextRelicFromFront(rs, Owner);
            RelicCmd.Obtain(rs, relic, Owner);
        }
        CardPileCmd.Add(rs, CardFactory.CreateRunCard(rs, GetStrikeForCharacter(Owner.CharacterId), Owner), PileType.Deck);
        CardPileCmd.Add(rs, CardFactory.CreateRunCard(rs, GetDefendForCharacter(Owner.CharacterId), Owner), PileType.Deck);
    }

    private static CardDef GetStrikeForCharacter(string characterId)
        => CardPools.ForCharacter(characterId).AllCards.First(c => c.Rarity == CardRarity.Basic && c.CanonicalTags.Contains(CardTag.Strike));

    private static CardDef GetDefendForCharacter(string characterId)
        => CardPools.ForCharacter(characterId).AllCards.First(c => c.Rarity == CardRarity.Basic && c.CanonicalTags.Contains(CardTag.Defend));
}
