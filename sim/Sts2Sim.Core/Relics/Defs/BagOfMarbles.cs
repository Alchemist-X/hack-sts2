namespace Sts2Sim.Core.Relics;

public sealed class BagOfMarbles : RelicModel
{
    public const string RelicId = "BAG_OF_MARBLES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<VulnerablePower>(1m)
    };

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && Owner.TurnNumber <= 1)
        {
            PowerCmd.Apply<VulnerablePower>(cs, HittableEnemies(cs), DynamicVars.Vulnerable.BaseValue, Owner, null);
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
