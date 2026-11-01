namespace Sts2Sim.Core.Relics;

public sealed class VeryHotCocoa : RelicModel
{
    public const string RelicId = "VERY_HOT_COCOA";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(4)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.BaseValue, Owner);
    }
}
