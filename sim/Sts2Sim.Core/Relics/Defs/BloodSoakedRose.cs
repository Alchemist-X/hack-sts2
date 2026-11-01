namespace Sts2Sim.Core.Relics;

public sealed class BloodSoakedRose : RelicModel
{
    public const string RelicId = "BLOOD_SOAKED_ROSE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public const string EnthralledCardId = "ENTHRALLED";

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.BaseValue;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1)
    };

    public override void AfterObtained(RunState rs)
    {
        CardPileCmd.AddCurseToDeck(rs, EnthralledCardId, Owner);
    }
}
