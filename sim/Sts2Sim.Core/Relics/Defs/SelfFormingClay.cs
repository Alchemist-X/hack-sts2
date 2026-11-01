namespace Sts2Sim.Core.Relics;

public sealed class SelfFormingClay : RelicModel
{
    public const string RelicId = "SELF_FORMING_CLAY";

    public const string SelfFormingClayPowerId = "SELF_FORMING_CLAY_POWER";

    public const string BlockNextTurnKey = "BlockNextTurn";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(BlockNextTurnKey, 3m)
    };

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress && ReferenceEquals(target, Owner) && result.UnblockedDamage > 0)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(SelfFormingClayPowerId), Owner, DynamicVars[BlockNextTurnKey].BaseValue, Owner, null);
        }
    }
}
