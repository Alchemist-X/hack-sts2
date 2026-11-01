namespace Sts2Sim.Core.Powers;

public sealed class StockPower : PowerModel
{
    public const string PowerId = "STOCK_POWER";

    public const string AxebotMonsterId = "AXEBOT";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner) || Amount <= 0)
        {
            return;
        }
        var cs = rs.Combat!;
        int stockAmount = Amount - 1;
        MonsterSpawner.SpawnForAdd(cs, MonsterRegistry.Get(AxebotMonsterId), Owner.Side, (Owner as MonsterState)?.SlotName, m => Axebot.GetExtra(m).StockOverrideAmount = stockAmount);
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;
}
