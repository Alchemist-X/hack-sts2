namespace Sts2Sim.Core.Relics;

public sealed class DarkstonePeriapt : RelicModel
{
    public const string RelicId = "DARKSTONE_PERIAPT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(6m)
    };

    public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.GetPileType(rs.Combat) == PileType.Deck && card.HasOwner && ReferenceEquals(card.Owner, Owner) && card.Type == CardType.Curse)
        {
            CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
        }
    }
}
