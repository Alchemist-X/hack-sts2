namespace Sts2Sim.Core.Relics;

public sealed class CharonsAshes : RelicModel
{
    public const string RelicId = "CHARONS_ASHES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(3m, ValueProp.Unpowered)
    };

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        var damage = DynamicVars.Damage;
        CreatureCmd.Damage(cs, HittableEnemies(cs), damage.BaseValue, damage.Props, Owner);
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
