namespace Sts2Sim.Core.Monsters;

public sealed class CrossbowRubyRaider : MonsterDef
{
    public const string MonsterIdEntry = "CROSSBOW_RUBY_RAIDER";
    public const string FireMoveId = "FIRE_MOVE";
    public const string ReloadMoveId = "RELOAD_MOVE";

    public const int MinInitialHpBase = 18;
    public const int MinInitialHpTough = 19;
    public const int MaxInitialHpBase = 21;
    public const int MaxInitialHpTough = 22;
    public const int FireDamageBase = 14;
    public const int FireDamageDeadly = 16;
    public const int ReloadBlock = 3;

    public CrossbowRubyRaider()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int FireDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FireDamageDeadly, FireDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var fire = new MoveStateDef(FireMoveId, FireMove, new SingleAttackIntent(FireDamage(cs)));
        var reload = new MoveStateDef(ReloadMoveId, ReloadMove, Intents.Defend);
        fire.FollowUpState = reload;
        reload.FollowUpState = fire;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { reload, fire }, reload);
    }

    private static void FireMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FireDamage(cs)).FromMonster(self).Execute();
    }

    private static void ReloadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, ReloadBlock, ValueProp.Move, null);
    }
}
