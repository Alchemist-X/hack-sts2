namespace Sts2Sim.Core.Powers;

public sealed class RavenousPower : PowerModel
{
    public const string PowerId = "RAVENOUS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented)
    {
        var owner = Owner;
        if (wasRemovalPrevented || ReferenceEquals(creature, owner) || creature.Side != owner.Side || owner.IsDead)
        {
            return;
        }
        if (owner is not MonsterState monster)
        {
            throw new InvalidOperationException($"{PowerId} is owned by a creature that is not a CorpseSlug");
        }
        CorpseSlug.GetExtra(monster).IsRavenous = true;
        CreatureCmd.StunWithMove(cs, monster, StunnedMove);
        PowerCmd.Apply<StrengthPower>(cs, monster, Amount, monster, null);
    }

    private static void StunnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CorpseSlug.GetExtra(self).IsRavenous = false;
    }
}
