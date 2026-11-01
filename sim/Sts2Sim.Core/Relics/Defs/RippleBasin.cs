namespace Sts2Sim.Core.Relics;

public sealed class RippleBasin : RelicModel
{
    public const string RelicId = "RIPPLE_BASIN";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(4m, ValueProp.Unpowered)
    };

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        if (cs.History.CardPlaysFinished.Any(e => e.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Attack && e.CardPlay.PlayerId == Owner.CombatId))
        {
            return;
        }
        Status = RelicStatus.Normal;
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.Type != CardType.Attack)
        {
            return;
        }
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        Status = RelicStatus.Normal;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        Status = RelicStatus.Active;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }
}
