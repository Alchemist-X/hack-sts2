namespace Sts2Sim.Core.Powers;

public sealed class NoxiousFumesPower : PowerModel
{
    public const string PowerId = "NOXIOUS_FUMES_POWER";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        for (int i = 0; i < hittable.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), hittable[i], Amount, Owner, null);
        }
    }
}
