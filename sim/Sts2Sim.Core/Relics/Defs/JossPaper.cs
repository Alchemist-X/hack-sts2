namespace Sts2Sim.Core.Relics;

public sealed class JossPaper : RelicModel
{
    public const string RelicId = "JOSS_PAPER";

    public const string ExhaustAmountKey = "ExhaustAmount";

    private bool _isActivating;
    private int _cardsExhausted;
    private int _etherealCount;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return CardsExhausted;
            }
            return DynamicVars[ExhaustAmountKey].IntValue;
        }
    }

    private bool IsActivating
    {
        get => _isActivating;
        set => _isActivating = value;
    }

    public int CardsExhausted
    {
        get => _cardsExhausted;
        set
        {
            _cardsExhausted = value;
            Status = _cardsExhausted == DynamicVars[ExhaustAmountKey].BaseValue - 1m ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    private int EtherealCount
    {
        get => _etherealCount;
        set => _etherealCount = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ExhaustAmountKey, 5m),
        new CardsVar(1)
    };

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        if (!causedByEthereal)
        {
            CardsExhausted++;
            DrawIfThresholdMet(cs);
            return;
        }
        EtherealCount++;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)))
        {
            return;
        }
        CardsExhausted += EtherealCount;
        EtherealCount = 0;
        DrawIfThresholdMet(cs);
    }

    private void DrawIfThresholdMet(CombatState cs)
    {
        decimal exhaustAmount = DynamicVars[ExhaustAmountKey].BaseValue;
        if (CardsExhausted < exhaustAmount)
        {
            return;
        }
        DoActivateVisuals();
        CardPileCmd.Draw(cs, (int)(CardsExhausted / exhaustAmount), Owner);
        CardsExhausted %= DynamicVars[ExhaustAmountKey].IntValue;
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        EtherealCount = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_cardsExhausted);
        w.WriteInt32(_etherealCount);
    }
}
