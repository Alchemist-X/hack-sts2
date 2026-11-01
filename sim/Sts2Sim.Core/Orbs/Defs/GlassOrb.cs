namespace Sts2Sim.Core.Orbs;

public sealed class GlassOrb : OrbModel
{
    public const string OrbIdEntry = "GLASS_ORB";
    public const decimal InitialPassiveValue = 4m;
    public const decimal EvokeMultiplier = 2m;
    public const decimal PassiveDecay = 1m;

    private decimal _passiveVal = InitialPassiveValue;

    public override string Id => OrbIdEntry;

    public override decimal PassiveVal(CombatState cs) => ModifyOrbValue(cs, _passiveVal);

    public override decimal EvokeVal(CombatState cs) => PassiveVal(cs) * EvokeMultiplier;

    public override void BeforeTurnEndOrbTrigger(CombatState cs) => TriggerPassive(cs, null);

    public override void Passive(CombatState cs, CreatureState? target)
    {
        var targets = StillHittable(cs, HittableEnemies(cs));
        decimal passiveVal = PassiveVal(cs);
        if (passiveVal <= 0m)
        {
            return;
        }
        _passiveVal = Math.Max(0m, _passiveVal - PassiveDecay);
        CreatureCmd.Damage(cs, targets, passiveVal, ValueProp.Unpowered, Owner, null, null);
    }

    public override IEnumerable<CreatureState> Evoke(CombatState cs)
    {
        var enemies = StillHittable(cs, HittableEnemies(cs));
        if (EvokeVal(cs) <= 0m)
        {
            return Array.Empty<CreatureState>();
        }
        CreatureCmd.Damage(cs, enemies, EvokeVal(cs), ValueProp.Unpowered, Owner, null, null);
        return enemies;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteTag(OrbCanonicalTags.GlassOrbExtra);
        w.WriteDecimal(_passiveVal);
    }

    private static List<CreatureState> StillHittable(CombatState cs, List<CreatureState> creatures)
    {
        var hittable = new List<CreatureState>(creatures.Count);
        for (int i = 0; i < creatures.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, creatures[i]))
            {
                hittable.Add(creatures[i]);
            }
        }
        return hittable;
    }
}
