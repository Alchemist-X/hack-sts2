namespace Sts2Sim.Core.Relics;

public sealed class Pocketwatch : RelicModel
{
    public const string RelicId = "POCKETWATCH";

    public const string CardThresholdKey = "CardThreshold";

    private int _cardsPlayedThisTurn;
    private int _cardsPlayedLastTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override int DisplayAmount => _cardsPlayedThisTurn;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CardThresholdKey, 3m),
        new CardsVar(3)
    };

    public int CardsPlayedThisTurn => _cardsPlayedThisTurn;

    public int CardsPlayedLastTurn => _cardsPlayedLastTurn;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            return;
        }
        if (!cs.IsInProgress)
        {
            return;
        }
        _cardsPlayedThisTurn++;
        RefreshCounter();
    }

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (Owner.TurnNumber == 1)
        {
            return count;
        }
        if (_cardsPlayedLastTurn > DynamicVars[CardThresholdKey].BaseValue)
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        _cardsPlayedLastTurn = _cardsPlayedThisTurn;
        _cardsPlayedThisTurn = 0;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        RefreshCounter();
    }

    private void RefreshCounter()
    {
        Status = _cardsPlayedThisTurn <= DynamicVars[CardThresholdKey].BaseValue ? RelicStatus.Active : RelicStatus.Normal;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        _cardsPlayedThisTurn = 0;
        _cardsPlayedLastTurn = 0;
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_cardsPlayedThisTurn);
        w.WriteInt32(_cardsPlayedLastTurn);
    }
}
