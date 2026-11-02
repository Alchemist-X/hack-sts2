namespace Sts2Sim.Core.Relics;

public sealed class BoundPhylactery : RelicModel
{
    public const string RelicId = "BOUND_PHYLACTERY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    public override bool SpawnsPets => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(1m)
    };

    public override void BeforeCombatStart(RunState rs)
    {
        SummonPet(rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart without combat"));
    }

    public override void AfterEnergyResetLate(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner) && Owner.TurnNumber != 1)
        {
            SummonPet(cs);
        }
    }

    private void SummonPet(CombatState cs)
    {
        OstyCmd.Summon(cs, Owner, DynamicVars.Summon.BaseValue, this);
    }
}
