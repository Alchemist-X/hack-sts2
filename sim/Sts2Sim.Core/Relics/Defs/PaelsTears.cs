namespace Sts2Sim.Core.Relics;

public sealed class PaelsTears : RelicModel
{
    public const string RelicId = "PAELS_TEARS";

    private bool _hadLeftoverEnergy;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    private bool HadLeftoverEnergy
    {
        get => _hadLeftoverEnergy;
        set => _hadLeftoverEnergy = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(2)
    };

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Contains(Owner))
        {
            return;
        }
        HadLeftoverEnergy = Owner.Energy > 0;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Contains(Owner) && HadLeftoverEnergy)
        {
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        }
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        HadLeftoverEnergy = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_hadLeftoverEnergy);
    }
}
