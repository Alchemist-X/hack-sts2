namespace Sts2Sim.Core.Relics;

public sealed class TwistedFunnel : RelicModel
{
    public const string RelicId = "TWISTED_FUNNEL";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, 4m)
    };

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Any(p => ReferenceEquals(p, Owner)) || Owner.TurnNumber > 1)
        {
            return;
        }
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        foreach (var enemy in enemies)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), enemy, DynamicVars[DynamicVarSet.PoisonName].IntValue, Owner, null);
        }
    }
}
