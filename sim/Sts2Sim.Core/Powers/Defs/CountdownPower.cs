namespace Sts2Sim.Core.Powers;

public sealed class CountdownPower : PowerModel
{
    public const string PowerId = "COUNTDOWN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var creature = cs.Rng.CombatTargets.NextItem(HittableEnemies(cs));
        if (creature != null)
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            PowerCmd.Apply<DoomPower>(cs, creature, Amount, Owner, null);
        }
    }

    internal static List<CreatureState> HittableEnemies(CombatState cs)
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
