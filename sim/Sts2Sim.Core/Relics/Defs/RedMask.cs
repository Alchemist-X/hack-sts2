namespace Sts2Sim.Core.Relics;

public sealed class RedMask : RelicModel
{
    public const string RelicId = "RED_MASK";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new PowerVar<WeakPower>(1m)
    };

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<WeakPower>(cs, hittable, DynamicVars.Weak.BaseValue, Owner, null);
    }
}
