namespace Sts2Sim.Core.Relics;

public sealed class SymbioticVirus : RelicModel
{
    public const string RelicId = "SYMBIOTIC_VIRUS";

    private const string DarknessKey = "Dark";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DarknessKey, 1m)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || Owner.TurnNumber > 1)
        {
            return;
        }
        for (int i = 0; i < DynamicVars[DarknessKey].BaseValue; i++)
        {
            OrbCmd.Channel<DarkOrb>(cs, Owner);
        }
    }
}
