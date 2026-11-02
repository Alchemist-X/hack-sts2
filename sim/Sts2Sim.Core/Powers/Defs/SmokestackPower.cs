namespace Sts2Sim.Core.Powers;

public sealed class SmokestackPower : PowerModel
{
    public const string PowerId = "SMOKESTACK_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        if (card.Type != CardType.Status || creator == null || !ReferenceEquals(creator, Owner))
        {
            return;
        }
        var hittableEnemies = new List<MonsterState>();
        cs.GetHittableEnemies(hittableEnemies);
        CreatureCmd.Damage(cs, hittableEnemies, Amount, ValueProp.Unpowered, Owner);
    }
}
