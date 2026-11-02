namespace Sts2Sim.Core.Powers;

public sealed class SurprisePower : PowerModel
{
    public const string PowerId = "SURPRISE_POWER";

    public const string FatGremlinId = "FAT_GREMLIN";

    public const string SneakyGremlinId = "SNEAKY_GREMLIN";

    public const string FatSlot = "fat";

    public const string SneakySlot = "sneaky";

    public const string GremlinMercNormalId = "GREMLIN_MERC_NORMAL";

    public const string HeistOnUnaddedCreatureSite = "SURPRISE_POWER.AfterDeath: PowerCmd.Apply(HeistPower) on the not-yet-added fat gremlin (Models.Powers/SurprisePower.cs:113-115) attaches the power directly; the sim's CanReceivePowers needs an attached creature, so the apply hooks do not fire";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(Owner, creature))
        {
            return;
        }
        var cs = rs.Combat!;
        var fatGremlin = MonsterSpawner.CreateCreature(cs, MonsterRegistry.Get(FatGremlinId), CombatSide.Enemy, FatSlot);
        int totalStolen = 0;
        foreach (var current in Owner.Powers.OfType<ThieveryPower>().ToList())
        {
            int intValue = current.Gold;
            totalStolen += intValue;
            var heistPower = new HeistPower
            {
                TargetId = current.TargetId
            };
            ApplyToUnaddedCreature(cs, heistPower, fatGremlin, intValue);
        }
        CreatureCmd.Add(cs, MonsterRegistry.Get(SneakyGremlinId), CombatSide.Enemy, SneakySlot);
        CreatureCmd.Add(cs, fatGremlin);
        if (totalStolen > 0 && cs.EncounterId == GremlinMercNormalId)
        {
            GremlinMercNormal.MarkGoldStolen(cs);
        }
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    private void ApplyToUnaddedCreature(CombatState cs, PowerModel power, MonsterState target, decimal amount)
    {
        if (CombatStatus.IsEnding(cs) || amount == 0m)
        {
            return;
        }
        cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, HeistOnUnaddedCreatureSite);
        power.ApplierId = Owner.CombatId;
        power.ApplyInternal(target, amount);
    }
}
