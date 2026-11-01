namespace Sts2Sim.Core.Relics;

public sealed class SealOfGold : RelicModel
{
    public const string RelicId = "SEAL_OF_GOLD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new GoldVar(3)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Contains(Owner) || Owner.Gold < DynamicVars.Gold.IntValue)
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
        PlayerCmd.LoseGold(cs, DynamicVars.Gold.IntValue, Owner);
    }
}
