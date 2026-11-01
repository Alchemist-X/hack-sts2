namespace Sts2Sim.Core.Relics;

public sealed class NutritiousOyster : RelicModel
{
    public const string RelicId = "NUTRITIOUS_OYSTER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new MaxHpVar(11m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
