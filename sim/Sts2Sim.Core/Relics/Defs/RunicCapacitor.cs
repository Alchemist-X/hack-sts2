namespace Sts2Sim.Core.Relics;

public sealed class RunicCapacitor : RelicModel
{
    public const string RelicId = "RUNIC_CAPACITOR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(3)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)) && Owner.TurnNumber <= 1)
        {
            OrbCmd.AddSlots(cs, Owner, DynamicVars.Repeat.IntValue);
        }
    }
}
