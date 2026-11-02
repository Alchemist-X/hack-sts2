namespace Sts2Sim.Core.Monsters;

public sealed class Stabbot : MonsterDef
{
    public const string MonsterIdEntry = "STABBOT";
    public const string StabMoveId = "STAB_MOVE";

    public const int MinInitialHpBase = 18;
    public const int MinInitialHpTough = 19;
    public const int MaxInitialHpBase = 23;
    public const int MaxInitialHpTough = 24;
    public const int StabDamageBase = 11;
    public const int StabDamageDeadly = 12;
    public const int StabFrail = 1;

    public Stabbot()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int StabDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StabDamageDeadly, StabDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var stab = new MoveStateDef(StabMoveId, StabMove, new SingleAttackIntent(StabDamage(cs)), new DebuffIntent());
        stab.FollowUpState = stab;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { stab }, stab);
    }

    private static void StabMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StabDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, StabFrail, self, null);
    }
}
