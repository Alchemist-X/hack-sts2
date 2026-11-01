namespace Sts2Sim.Core.Powers;

public sealed class SteamEruptionPower : PowerModel
{
    public const string PowerId = "STEAM_ERUPTION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        if (creature is MonsterState monster && monster.Def is WaterfallGiant)
        {
            WaterfallGiant.TriggerAboutToBlowState(rs.Combat!, monster);
        }
    }

    public override bool ShouldStopCombatFromEnding(CombatState cs) => true;

    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);

    public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;
}
