namespace Sts2Sim.Core.Powers;

public sealed class ThunderPower : PowerModel
{
    public const string PowerId = "THUNDER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)
    {
        if (!orb.HasOwner || !ReferenceEquals(orb.Owner, Owner as PlayerState) || orb is not LightningOrb)
        {
            return;
        }
        var livingTargets = new List<CreatureState>();
        foreach (var c in targets)
        {
            if (c.IsAlive)
            {
                livingTargets.Add(c);
            }
        }
        CreatureCmd.Damage(cs, livingTargets, Amount, ValueProp.Unpowered, Owner, null, null);
    }
}
