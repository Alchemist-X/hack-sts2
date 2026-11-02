namespace Sts2Sim.Core.Relics;

public sealed class DiamondDiadem : RelicModel
{
    public const string RelicId = "DIAMOND_DIADEM";

    public const string BlurPowerId = "BLUR_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(20m, ValueProp.Unpowered)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)) && Owner.TurnNumber <= 1)
        {
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
            PowerCmd.Apply(cs, PowerRegistry.Create(BlurPowerId), Owner, 1m, Owner, null);
        }
    }
}
