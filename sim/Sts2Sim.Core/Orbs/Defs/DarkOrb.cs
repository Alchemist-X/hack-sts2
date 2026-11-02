namespace Sts2Sim.Core.Orbs;

public sealed class DarkOrb : OrbModel
{
    public const string OrbIdEntry = "DARK_ORB";
    public const decimal BasePassiveValue = 6m;
    public const decimal InitialEvokeValue = 6m;

    private decimal _evokeVal = InitialEvokeValue;

    public override string Id => OrbIdEntry;

    public override decimal PassiveVal(CombatState cs) => ModifyOrbValue(cs, BasePassiveValue);

    public override decimal EvokeVal(CombatState cs) => _evokeVal;

    public override void BeforeTurnEndOrbTrigger(CombatState cs) => TriggerPassive(cs, null);

    public override void Passive(CombatState cs, CreatureState? target)
    {
        if (target != null)
        {
            throw new InvalidOperationException("Dark orbs cannot target creatures.");
        }
        _evokeVal += PassiveVal(cs);
    }

    public override IEnumerable<CreatureState> Evoke(CombatState cs)
    {
        var hittableEnemies = HittableEnemies(cs);
        if (hittableEnemies.Count == 0)
        {
            return Array.Empty<CreatureState>();
        }
        var weakestEnemy = hittableEnemies[0];
        for (int i = 1; i < hittableEnemies.Count; i++)
        {
            if (hittableEnemies[i].CurrentHp < weakestEnemy.CurrentHp)
            {
                weakestEnemy = hittableEnemies[i];
            }
        }
        CreatureCmd.Damage(cs, weakestEnemy, EvokeVal(cs), ValueProp.Unpowered, Owner, null, null);
        return new[] { weakestEnemy };
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteTag(OrbCanonicalTags.DarkOrbExtra);
        w.WriteDecimal(_evokeVal);
    }
}
