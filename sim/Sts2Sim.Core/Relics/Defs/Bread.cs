namespace Sts2Sim.Core.Relics;

public sealed class Bread : RelicModel
{
    public const string RelicId = "BREAD";

    public const string GainEnergyKey = "GainEnergy";

    public const string LoseEnergyKey = "LoseEnergy";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(GainEnergyKey, 1),
        new EnergyVar(LoseEnergyKey, 2)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        if (Owner.TurnNumber == 1)
        {
            return amount;
        }
        return amount + DynamicVars[GainEnergyKey].BaseValue;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Owner.TurnNumber == 1)
        {
            PlayerCmd.LoseEnergy(cs, DynamicVars[LoseEnergyKey].BaseValue, Owner);
        }
    }
}
