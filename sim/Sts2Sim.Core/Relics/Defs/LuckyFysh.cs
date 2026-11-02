namespace Sts2Sim.Core.Relics;

public sealed class LuckyFysh : RelicModel
{
    public const string RelicId = "LUCKY_FYSH";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool IsAllowedInShops => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(15)
    };

    public override bool IsAllowed(RunState rs) => RelicCatalog.IsBeforeAct3TreasureChest(rs);

    public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.GetPileType(rs.Combat) == PileType.Deck && card.HasOwner && ReferenceEquals(card.Owner, Owner))
        {
            PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
        }
    }
}
