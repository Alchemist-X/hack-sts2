namespace Sts2Sim.Core.Relics;

public sealed class FakeHappyFlower : RelicModel
{
    public const string RelicId = "FAKE_HAPPY_FLOWER";

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

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new DynamicVar(TurnsKey, 5m)
    };

    private bool IsActivating
    {
        get => _isActivating;
        set => _isActivating = value;
    }

    public int TurnsSeen
    {
        get => _turnsSeen;
        set => _turnsSeen = value;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        TurnsSeen = (TurnsSeen + 1) % DynamicVars[TurnsKey].IntValue;
        Status = TurnsSeen == DynamicVars[TurnsKey].IntValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        if (TurnsSeen == 0)
        {
            DoActivateVisuals();
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
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
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_turnsSeen);
    }
}
