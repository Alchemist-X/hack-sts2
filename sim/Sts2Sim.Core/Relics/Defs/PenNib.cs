namespace Sts2Sim.Core.Relics;

public sealed class PenNib : RelicModel
{
    public const string RelicId = "PEN_NIB";

    public const int AttacksThreshold = 10;

    public const int NoCard = -1;

    private bool _isActivating;
    private int _attacksPlayed;
    private int _attackToDoubleId = NoCard;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return AttacksPlayed % AttacksThreshold;
            }
            return AttacksThreshold;
        }
    }

    private bool IsActivating
    {
        get => _isActivating;
        set
        {
            _isActivating = value;
            UpdateDisplay();
        }
    }

    public int AttacksPlayed
    {
        get => _attacksPlayed;
        private set
        {
            _attacksPlayed = value % AttacksThreshold;
            UpdateDisplay();
        }
    }

    public int AttackToDoubleId => _attackToDoubleId;

    private void UpdateDisplay()
    {
        if (IsActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            Status = AttacksPlayed == AttacksThreshold - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public void NotifyAttackPlayed()
    {
        AttacksPlayed++;
        if (AttacksPlayed == 0)
        {
            DoActivateVisuals();
        }
    }

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        var osty = rs.Combat == null ? null : Owner.GetOsty(rs.Combat);
        if (!ReferenceEquals(dealer, Owner) && !ReferenceEquals(dealer, osty))
        {
            return 1m;
        }
        if (_attackToDoubleId == NoCard)
        {
            var pile = cardSource.GetCombatPile(rs.Combat);
            if ((pile == null || pile.Type != PileType.Play) && AttacksPlayed == AttacksThreshold - 1)
            {
                return 2m;
            }
            return 1m;
        }
        if (cardSource.InstanceId == _attackToDoubleId)
        {
            return 2m;
        }
        return 1m;
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.Type != CardType.Attack)
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner))
        {
            return;
        }
        NotifyAttackPlayed();
        if (AttacksPlayed == 0)
        {
            _attackToDoubleId = card.InstanceId;
        }
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (_attackToDoubleId == NoCard)
        {
            return;
        }
        if (cardPlay.Card.InstanceId != _attackToDoubleId)
        {
            return;
        }
        _attackToDoubleId = NoCard;
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_attacksPlayed);
        w.WriteInt32(_attackToDoubleId);
    }
}
