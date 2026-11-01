namespace Sts2Sim.Core.Relics;

public sealed class Sai : RelicModel
{
    public const string RelicId = "SAI";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(7m, ValueProp.Unpowered)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }
}
