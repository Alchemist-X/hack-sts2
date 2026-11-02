namespace Sts2Sim.Core.Relics;

public sealed class LeesWaffle : RelicModel
{
    public const string RelicId = "LEES_WAFFLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(7m)
    };

    public override void AfterObtained(RunState rs)
    {
        var creature = Owner;
        CreatureCmd.GainMaxHp(rs, creature, DynamicVars.MaxHp.BaseValue);
        CreatureCmd.Heal(rs, creature, creature.MaxHp - creature.CurrentHp);
    }
}
