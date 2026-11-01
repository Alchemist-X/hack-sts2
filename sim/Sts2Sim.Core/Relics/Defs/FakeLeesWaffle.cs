namespace Sts2Sim.Core.Relics;

public sealed class FakeLeesWaffle : RelicModel
{
    public const string RelicId = "FAKE_LEES_WAFFLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasUponPickupEffect => true;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(10m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.Heal(rs, Owner, Owner.MaxHp * (DynamicVars.Heal.BaseValue / 100m));
    }
}
