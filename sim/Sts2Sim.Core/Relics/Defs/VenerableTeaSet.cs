namespace Sts2Sim.Core.Relics;

public sealed class VenerableTeaSet : RelicModel
{
    public const string RelicId = "VENERABLE_TEA_SET";

    private bool _gainEnergyInNextCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(2)
    };

    public bool GainEnergyInNextCombat
    {
        get => _gainEnergyInNextCombat;
        set
        {
            if (_gainEnergyInNextCombat != value)
            {
                _gainEnergyInNextCombat = value;
                Status = _gainEnergyInNextCombat ? RelicStatus.Active : RelicStatus.Normal;
            }
        }
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not RestSiteRoom)
        {
            return;
        }
        GainEnergyInNextCombat = true;
    }

    public override void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(Owner, player) && GainEnergyInNextCombat)
        {
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
            GainEnergyInNextCombat = false;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_gainEnergyInNextCombat);
    }
}
