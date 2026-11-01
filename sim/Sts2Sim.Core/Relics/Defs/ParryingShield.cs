namespace Sts2Sim.Core.Relics;

public sealed class ParryingShield : RelicModel
{
    public const string RelicId = "PARRYING_SHIELD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(10m, ValueProp.Unpowered),
        new DamageVar(6m, ValueProp.Unpowered)
    };

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner) || Owner.Block < DynamicVars.Block.BaseValue)
        {
            return;
        }
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        var creature = cs.Rng.CombatTargets.NextItem(hittable);
        if (creature != null)
        {
            CreatureCmd.Damage(cs, creature, DynamicVars.Damage, Owner);
        }
    }
}
