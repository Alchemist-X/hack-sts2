namespace Sts2Sim.Core.Relics;

public sealed class OrnamentalFan : RelicModel
{
    public const string RelicId = "ORNAMENTAL_FAN";

    private bool _isActivating;
    private int _attacksPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

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
        new BlockVar(4m, ValueProp.Unpowered)
    };

    public int AttacksPlayedThisTurn => _attacksPlayedThisTurn;

    private void SetIsActivating(bool inProgress, bool value)
    {
        _isActivating = value;
        UpdateDisplay(inProgress);
    }

    private void SetAttacksPlayedThisTurn(bool inProgress, int value)
    {
        _attacksPlayedThisTurn = value;
        UpdateDisplay(inProgress);
    }

    private void UpdateDisplay(bool inProgress)
    {
        if (_isActivating || !inProgress)
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
        SetAttacksPlayedThisTurn(cs.IsInProgress, 0);
        Status = RelicStatus.Normal;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || !cs.IsInProgress || card.Type != CardType.Attack)
        {
            return;
        }
        SetAttacksPlayedThisTurn(cs.IsInProgress, _attacksPlayedThisTurn + 1);
        int intValue = DynamicVars.Cards.IntValue;
        if (_attacksPlayedThisTurn % intValue == 0)
        {
            DoActivateVisuals(cs.IsInProgress);
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
        }
    }

    private void DoActivateVisuals(bool inProgress)
    {
        SetIsActivating(inProgress, true);
        SetIsActivating(inProgress, false);
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        SetIsActivating(rs.Combat?.IsInProgress ?? false, false);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_attacksPlayedThisTurn);
    }
}
