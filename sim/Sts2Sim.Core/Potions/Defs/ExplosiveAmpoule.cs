namespace Sts2Sim.Core.Potions;

public sealed class ExplosiveAmpoule : PotionModel
{
    public const string PotionId = "EXPLOSIVE_AMPOULE";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AllEnemies;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(10m, ValueProp.Unpowered)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        var cs = RequireCombat(rs);
        var player = Owner;
        var damage = DynamicVars.Damage;
        var targets = HittableEnemies(cs);
        CreatureCmd.Damage(cs, targets, damage.BaseValue, damage.Props, player, null, null);
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
