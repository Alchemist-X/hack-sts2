namespace Sts2Sim.Core.Relics;

public sealed class Chandelier : RelicModel
{
    public const string RelicId = "CHANDELIER";

    public const int EnergyTurn = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(3)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Owner.TurnNumber == EnergyTurn)
        {
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        }
    }
}
