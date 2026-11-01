namespace Sts2Sim.Core.Relics;

public sealed class Ectoplasm : RelicModel
{
    public const string RelicId = "ECTOPLASM";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(1)
    };

    public override decimal ModifyGoldGained(RunState rs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return 0m;
    }

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }
}
