namespace Sts2Sim.Core.Relics;

public sealed class ForgottenSoul : RelicModel
{
    public const string RelicId = "FORGOTTEN_SOUL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(1m, ValueProp.Unpowered)
    };

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return;
        }
        var creature = cs.Rng.CombatTargets.NextItem(HittableEnemies(cs));
        if (creature != null)
        {
            CreatureCmd.Damage(cs, creature, DynamicVars.Damage, Owner);
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
