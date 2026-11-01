namespace Sts2Sim.Core.Powers;

public sealed class InfestedPower : PowerModel
{
    public const string PowerId = "INFESTED_POWER";

    public const string WrigglerMonsterId = "WRIGGLER";

    public const int WrigglerCount = 4;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(Owner, creature))
        {
            return;
        }
        throw new NotImplementedException("W3 gap: CreatureCmd.Add has no seam to set Wriggler.StartStunned on the monster model before it is added (Models.Powers/InfestedPower.cs:81-83)");
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    public static string GetWrigglerSlotName(int index) => "wriggler" + (index + 1);
}
