namespace Sts2Sim.Core.Relics;

public sealed class MusicBox : RelicModel
{
    public const string RelicId = "MUSIC_BOX";

    public const int NoCard = -1;

    private bool _wasUsedThisTurn;
    private int _cardBeingPlayedId = NoCard;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    private bool WasUsedThisTurn
    {
        get => _wasUsedThisTurn;
        set => _wasUsedThisTurn = value;
    }

    private int CardBeingPlayedId
    {
        get => _cardBeingPlayedId;
        set => _cardBeingPlayedId = value;
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (CardBeingPlayedId != NoCard)
        {
            return;
        }
        if (!ReferenceEquals(cardPlay.Card.Owner, Owner))
        {
            return;
        }
        if (WasUsedThisTurn)
        {
            return;
        }
        if (cardPlay.Card.Type != CardType.Attack)
        {
            return;
        }
        CardBeingPlayedId = cardPlay.Card.InstanceId;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (CardBeingPlayedId == NoCard || cardPlay.Card.InstanceId != CardBeingPlayedId)
        {
            return;
        }
        var card = cs.CloneCard(cardPlay.Card);
        card.ExhaustOnNextPlay = false;
        CardCmd.ApplyKeyword(card, CardKeyword.Ethereal);
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, Owner);
        WasUsedThisTurn = true;
        CardBeingPlayedId = NoCard;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Contains(Owner))
        {
            return;
        }
        WasUsedThisTurn = false;
        CardBeingPlayedId = NoCard;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        WasUsedThisTurn = false;
        CardBeingPlayedId = NoCard;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_wasUsedThisTurn);
        w.WriteInt32(_cardBeingPlayedId);
    }
}
