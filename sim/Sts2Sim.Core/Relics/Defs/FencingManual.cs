namespace Sts2Sim.Core.Relics;

public sealed class FencingManual : RelicModel
{
    public const string RelicId = "FENCING_MANUAL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(10)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)) && Owner.TurnNumber <= 1)
        {
            ForgeCmd.Forge(cs, DynamicVars.Forge.BaseValue, Owner, this);
        }
    }
}
