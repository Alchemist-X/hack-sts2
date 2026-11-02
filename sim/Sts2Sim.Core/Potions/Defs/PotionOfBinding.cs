namespace Sts2Sim.Core.Potions;

public sealed class PotionOfBinding : PotionModel
{
    public const string PotionId = "POTION_OF_BINDING";

    public const string VulnerablePowerId = "VULNERABLE_POWER";

    public const string WeakPowerId = "WEAK_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AllEnemies;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.VulnerableName, 1m),
        new DynamicVar(DynamicVarSet.WeakName, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        var cs = RequireCombat(rs);
        var targets = HittableEnemies(cs);
        decimal weakAmount = DynamicVars[DynamicVarSet.VulnerableName].IntValue;
        for (int i = 0; i < targets.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(WeakPowerId), targets[i], weakAmount, Owner, null);
        }
        decimal vulnerableAmount = DynamicVars[DynamicVarSet.WeakName].IntValue;
        for (int i = 0; i < targets.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(VulnerablePowerId), targets[i], vulnerableAmount, Owner, null);
        }
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
