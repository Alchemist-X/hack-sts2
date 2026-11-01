namespace Sts2Sim.Core.Relics;

public sealed class Candelabra : RelicModel
{
    public const string RelicId = "CANDELABRA";

    public const int EnergyTurn = 2;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(2)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Owner.TurnNumber == EnergyTurn)
        {
            PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        }
    }
}
