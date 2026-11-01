namespace Sts2Sim.Core.Relics;

public sealed class Pendulum : RelicModel
{
    public const string RelicId = "PENDULUM";

    public const string TurnsKey = "Turns";

    private bool _isActivating;
    private int _turnsSeen;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!_isActivating)
            {
                return TurnsSeen;
            }
            return DynamicVars[TurnsKey].IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1),
        new DynamicVar(TurnsKey, 3m)
    };

    public int TurnsSeen
    {
        get => _turnsSeen;
        set => _turnsSeen = value;
    }

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        TurnsSeen = (TurnsSeen + 1) % DynamicVars[TurnsKey].IntValue;
        Status = TurnsSeen == DynamicVars[TurnsKey].IntValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        if (TurnsSeen == 0)
        {
            DoActivateVisuals();
        }
    }

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (TurnsSeen != 0)
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }

    private void DoActivateVisuals()
    {
        _isActivating = true;
        _isActivating = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_turnsSeen);
    }
}
