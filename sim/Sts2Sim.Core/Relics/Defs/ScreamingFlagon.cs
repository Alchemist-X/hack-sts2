namespace Sts2Sim.Core.Relics;

public sealed class ScreamingFlagon : RelicModel
{
    public const string RelicId = "SCREAMING_FLAGON";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DamageVar(20m, ValueProp.Unpowered)
    };

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner) || !Owner.Hand.IsEmpty)
        {
            return;
        }
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        CreatureCmd.Damage(cs, hittable, DynamicVars.Damage, Owner);
    }
}
