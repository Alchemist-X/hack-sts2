namespace Sts2Sim.Core.Relics;

public sealed class PollinousCore : RelicModel
{
    public const string RelicId = "POLLINOUS_CORE";

    public const string TurnsKey = "Turns";

    private bool _isActivating;
    private int _turnsSeen;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return TurnsSeen;
            }
            return DynamicVars[TurnsKey].IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2),
        new DynamicVar(TurnsKey, 4m)
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

    public int TurnsSeen
    {
        get => _turnsSeen;
        set
        {
            _turnsSeen = value;
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
            int intValue = DynamicVars[TurnsKey].IntValue;
            Status = TurnsSeen == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        TurnsSeen++;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (TurnsSeen < DynamicVars[TurnsKey].IntValue)
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }

    public override void AfterModifyingHandDraw(CombatState cs)
    {
        TurnsSeen = 0;
        DoActivateVisuals();
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_turnsSeen);
    }
}
