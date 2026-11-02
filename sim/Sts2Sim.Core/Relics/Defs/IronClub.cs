namespace Sts2Sim.Core.Relics;

public sealed class IronClub : RelicModel
{
    public const string RelicId = "IRON_CLUB";

    private bool _isActivating;
    private int _cardsPlayed;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return CardsPlayed % DynamicVars.Cards.IntValue;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(4)
    };

    private bool IsActivating
    {
        get => _isActivating;
        set
        {
            _isActivating = value;
            UpdateDisplay();
        }
    }

    public int CardsPlayed
    {
        get => _cardsPlayed;
        set
        {
            _cardsPlayed = value;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (IsActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            int intValue = DynamicVars.Cards.IntValue;
            Status = CardsPlayed % intValue == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        CardsPlayed++;
        int intValue = DynamicVars.Cards.IntValue;
        if (cs.IsInProgress && CardsPlayed % intValue == 0)
        {
            DoActivateVisuals();
            CardPileCmd.Draw(cs, 1m, Owner);
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_cardsPlayed);
    }
}
