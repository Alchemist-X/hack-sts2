namespace Sts2Sim.Core.Relics;

public sealed class Strawberry : RelicModel
{
    public const string RelicId = "STRAWBERRY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new MaxHpVar(7m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
