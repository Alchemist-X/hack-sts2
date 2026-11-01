namespace Sts2Sim.Core.Relics;

public sealed class LoomingFruit : RelicModel
{
    public const string RelicId = "LOOMING_FRUIT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(31m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
