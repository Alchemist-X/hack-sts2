namespace Sts2Sim.Core.Relics;

public sealed class MercuryHourglass : RelicModel
{
    public const string RelicId = "MERCURY_HOURGLASS";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(3m, ValueProp.Unpowered)
    };

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner))
        {
            CreatureCmd.Damage(cs, HittableEnemies(cs), DynamicVars.Damage, Owner);
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
