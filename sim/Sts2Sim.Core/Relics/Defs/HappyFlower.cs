namespace Sts2Sim.Core.Relics;

public sealed class HappyFlower : RelicModel
{
    public const string RelicId = "HAPPY_FLOWER";

    public const int TurnsThreshold = 3;

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
            if (!IsActivating)
            {
                return TurnsSeen;
            }
            return DynamicVars[TurnsKey].IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.EnergyName, 1m),
        new DynamicVar(TurnsKey, 3m)
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
        if (!Contains(participants, Owner))
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

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_turnsSeen);
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    private static bool Contains(IReadOnlyList<CreatureState> participants, CreatureState creature)
    {
        for (int i = 0; i < participants.Count; i++)
        {
            if (ReferenceEquals(participants[i], creature))
            {
                return true;
            }
        }
        return false;
    }
}
