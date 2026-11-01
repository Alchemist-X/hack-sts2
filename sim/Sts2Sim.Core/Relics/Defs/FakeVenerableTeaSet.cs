namespace Sts2Sim.Core.Relics;

public sealed class FakeVenerableTeaSet : RelicModel
{
    public const string RelicId = "FAKE_VENERABLE_TEA_SET";

    private bool _gainEnergyInNextCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1)
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
