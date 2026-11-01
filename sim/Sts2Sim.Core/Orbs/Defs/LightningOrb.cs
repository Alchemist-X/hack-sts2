namespace Sts2Sim.Core.Orbs;

public sealed class LightningOrb : OrbModel
{
    public const string OrbIdEntry = "LIGHTNING_ORB";
    public const decimal BasePassiveValue = 3m;
    public const decimal BaseEvokeValue = 8m;

    public override string Id => OrbIdEntry;

    public override decimal PassiveVal(CombatState cs) => ModifyOrbValue(cs, BasePassiveValue);

    public override decimal EvokeVal(CombatState cs) => ModifyOrbValue(cs, BaseEvokeValue);

    public override void BeforeTurnEndOrbTrigger(CombatState cs) => TriggerPassive(cs, null);

    public override void Passive(CombatState cs, CreatureState? target)
    {
        ApplyLightningDamage(cs, PassiveVal(cs), target);
    }

    public override IEnumerable<CreatureState> Evoke(CombatState cs) => ApplyLightningDamage(cs, EvokeVal(cs), null);

    private IReadOnlyList<CreatureState> ApplyLightningDamage(CombatState cs, decimal value, CreatureState? target)
    {
        var owner = Owner;
        var hittable = HittableOpponentsOf(cs, owner);
        if (hittable.Count == 0)
        {
            return Array.Empty<CreatureState>();
        }
        var targets = new[] { target ?? cs.Rng.CombatTargets.NextItem(hittable)! };
        CreatureCmd.Damage(cs, targets, value, ValueProp.Unpowered, owner, null, null);
        return targets;
    }
}
