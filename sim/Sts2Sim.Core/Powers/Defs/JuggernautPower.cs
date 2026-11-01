namespace Sts2Sim.Core.Powers;

public sealed class JuggernautPower : PowerModel
{
    public const string PowerId = "JUGGERNAUT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        if (amount <= 0m || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        var hittableEnemies = new List<MonsterState>();
        cs.GetHittableEnemies(hittableEnemies);
        if (hittableEnemies.Count == 0)
        {
            return;
        }
        var target = cs.Rng.CombatTargets.NextItem(hittableEnemies)!;
        CreatureCmd.Damage(cs, target, Amount, ValueProp.Unpowered, Owner);
    }
}
