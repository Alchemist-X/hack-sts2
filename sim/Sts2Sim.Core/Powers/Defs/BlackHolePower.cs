namespace Sts2Sim.Core.Powers;

public sealed class BlackHolePower : PowerModel
{
    public const string PowerId = "BLACK_HOLE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Resources.StarsSpent > 0 && ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState) && cardPlay.IsLastInSeries)
        {
            DealDamageToAllEnemies(cs);
        }
    }

    public override void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)
    {
        if (amount > 0 && ReferenceEquals(gainer, Owner as PlayerState))
        {
            DealDamageToAllEnemies(cs);
        }
    }

    private void DealDamageToAllEnemies(CombatState cs)
    {
        using var blocking = cs.Choice.PushContext(ChoiceContextKind.Inline);
        CreatureCmd.Damage(cs, HittableEnemies(cs), Amount, ValueProp.Unpowered, Owner);
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }
}
