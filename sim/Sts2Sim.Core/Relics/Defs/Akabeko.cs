namespace Sts2Sim.Core.Relics;

public sealed class Akabeko : RelicModel
{
    public const string RelicId = "AKABEKO";

    public const string VigorPowerId = "VIGOR_POWER";

    public const string VigorKey = "VigorPower";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(VigorKey, 8m)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)) && Owner.TurnNumber <= 1)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(VigorPowerId), Owner, DynamicVars[VigorKey].IntValue, Owner, null);
        }
    }
}
