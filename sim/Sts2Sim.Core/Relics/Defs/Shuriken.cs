namespace Sts2Sim.Core.Relics;

public sealed class Shuriken : RelicModel
{
    public const string RelicId = "SHURIKEN";

    private bool _isActivating;
    private int _attacksPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override int DisplayAmount
    {
        get
        {
            if (!_isActivating)
            {
                return _attacksPlayedThisTurn % DynamicVars.Cards.IntValue;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new PowerVar<StrengthPower>(1m)
    };

    public int AttacksPlayedThisTurn => _attacksPlayedThisTurn;

    private void SetIsActivating(bool value)
    {
        _isActivating = value;
        UpdateDisplay();
    }

    private void SetAttacksPlayedThisTurn(int value)
    {
        _attacksPlayedThisTurn = value;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_isActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            int intValue = DynamicVars.Cards.IntValue;
            Status = _attacksPlayedThisTurn % intValue == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        SetAttacksPlayedThisTurn(0);
        Status = RelicStatus.Normal;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || !cs.IsInProgress || card.Type != CardType.Attack)
        {
            return;
        }
        SetAttacksPlayedThisTurn(_attacksPlayedThisTurn + 1);
        int intValue = DynamicVars.Cards.IntValue;
        if (_attacksPlayedThisTurn % intValue == 0)
        {
            DoActivateVisuals();
            PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
        }
    }

    private void DoActivateVisuals()
    {
        SetIsActivating(true);
        SetIsActivating(false);
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        SetAttacksPlayedThisTurn(0);
        SetIsActivating(false);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_attacksPlayedThisTurn);
    }
}
