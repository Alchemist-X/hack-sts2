namespace Sts2Sim.Core.Relics;

public sealed class Sozu : RelicModel
{
    public const string RelicId = "SOZU";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1)
    };

    public override bool ShouldProcurePotion(RunState rs, PotionModel potion, PlayerState player) => !ReferenceEquals(player, Owner);

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }
}
