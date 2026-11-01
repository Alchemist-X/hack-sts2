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
        var cs = rs.Combat!;
        var def = MonsterRegistry.Get(WrigglerMonsterId);
        for (int i = 0; i < WrigglerCount; i++)
        {
            MonsterSpawner.SpawnForAdd(cs, def, Owner.Side, GetWrigglerSlotName(i), static m => Wriggler.GetExtra(m).StartStunned = true);
        }
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    public static string GetWrigglerSlotName(int index) => "wriggler" + (index + 1);
}
