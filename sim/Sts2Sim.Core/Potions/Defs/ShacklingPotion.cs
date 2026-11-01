namespace Sts2Sim.Core.Potions;

public sealed class ShacklingPotion : PotionModel
{
    public const string PotionId = "SHACKLING_POTION";

    public const string ShacklingPotionPowerId = "SHACKLING_POTION_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AllEnemies;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(7m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        var cs = RequireCombat(rs);
        var creature = Owner;
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        decimal amount = DynamicVars.Strength.IntValue;
        for (int i = 0; i < enemies.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(ShacklingPotionPowerId), enemies[i], amount, creature, null);
        }
    }
}
