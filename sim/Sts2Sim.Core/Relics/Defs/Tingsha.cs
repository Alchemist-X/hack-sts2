namespace Sts2Sim.Core.Relics;

public sealed class Tingsha : RelicModel
{
    public const string RelicId = "TINGSHA";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DamageVar(3m, ValueProp.Unpowered)
    };

    public override void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || Owner.Side != cs.CurrentSide)
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
