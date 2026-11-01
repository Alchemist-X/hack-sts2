namespace Sts2Sim.Core.Relics;

public sealed class DaughterOfTheWind : RelicModel
{
    public const string RelicId = "DAUGHTER_OF_THE_WIND";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(1m, ValueProp.Unpowered)
    };

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.Type == CardType.Attack && card.HasOwner && ReferenceEquals(card.Owner, Owner))
        {
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
        }
    }
}
