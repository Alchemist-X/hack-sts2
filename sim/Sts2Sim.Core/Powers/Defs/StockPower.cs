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
        throw new NotImplementedException("W3 gap: CreatureCmd.Add has no seam to set Axebot.StockAmount on the monster model before it is added (Models.Powers/StockPower.cs:58-61)");
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;
}
