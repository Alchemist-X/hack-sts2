namespace Sts2Sim.Core.Powers;

public sealed class RollingBoulderPower : PowerModel
{
    public const string PowerId = "ROLLING_BOULDER_POWER";

    public const int DamageIncrement = 5;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        CreatureCmd.Damage(cs, hittable, Amount, ValueProp.Unpowered, Owner);
        SetAmount(Amount + DamageIncrement);
    }
}
