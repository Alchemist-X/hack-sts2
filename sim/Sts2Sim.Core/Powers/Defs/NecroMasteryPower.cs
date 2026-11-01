namespace Sts2Sim.Core.Powers;

public sealed class NecroMasteryPower : PowerModel
{
    public const string PowerId = "NECRO_MASTERY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCurrentHpChanged(RunState rs, CreatureState creature, decimal delta)
    {
        if (delta >= 0m)
        {
            return;
        }
        if (creature is not MonsterState monster || !string.Equals(monster.MonsterId, CombatStatus.OstyMonsterId, StringComparison.Ordinal))
        {
            return;
        }
        if (Owner is not PlayerState || monster.PetOwnerId != Owner.CombatId)
        {
            return;
        }
        var cs = rs.Combat ?? throw new InvalidOperationException($"{PowerId} reacts to Osty HP loss outside combat");
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        CreatureCmd.Damage(cs, hittable, -delta * Amount, ValueProp.Unblockable | ValueProp.Unpowered, Owner);
    }
}
