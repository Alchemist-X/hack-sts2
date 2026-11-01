namespace Sts2Sim.Core.Relics;

public sealed class FakeMango : RelicModel
{
    public const string RelicId = "FAKE_MANGO";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasUponPickupEffect => true;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(3m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
    }
}
