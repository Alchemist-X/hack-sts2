namespace Sts2Sim.Core.Relics;

public sealed class Kunai : RelicModel
{
    public const string RelicId = "KUNAI";

    private bool _isActivating;
    private int _attacksPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return AttacksPlayedThisTurn % DynamicVars.Cards.IntValue;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new PowerVar<DexterityPower>(1m)
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

    private int AttacksPlayedThisTurn
    {
        get => _attacksPlayedThisTurn;
        set
        {
            _attacksPlayedThisTurn = value;
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
            Status = AttacksPlayedThisTurn % intValue == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        AttacksPlayedThisTurn = 0;
        Status = RelicStatus.Normal;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || !cs.IsInProgress || card.Type != CardType.Attack)
        {
            return;
        }
        AttacksPlayedThisTurn++;
        int intValue = DynamicVars.Cards.IntValue;
        if (AttacksPlayedThisTurn % intValue == 0)
        {
            DoActivateVisuals();
            PowerCmd.Apply<DexterityPower>(cs, Owner, DynamicVars.Dexterity.BaseValue, Owner, null);
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        AttacksPlayedThisTurn = 0;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_attacksPlayedThisTurn);
    }
}
