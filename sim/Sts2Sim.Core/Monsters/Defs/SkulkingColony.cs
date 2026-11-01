namespace Sts2Sim.Core.Monsters;

public sealed class SkulkingColony : MonsterDef
{
    public const string MonsterIdEntry = "SKULKING_COLONY";
    public const string ZoomMoveId = "ZOOM_MOVE";
    public const string Zoom2MoveId = "ZOOM_MOVE_2";
    public const string InertiaMoveId = "INERTIA_MOVE";
    public const string PiercingStabsMoveId = "PIERCING_STABS_MOVE";

    public const int InitialHpBase = 75;
    public const int InitialHpTough = 80;
    public const int InertiaDamageBase = 9;
    public const int InertiaDamageDeadly = 11;
    public const int ZoomDamageBase = 14;
    public const int ZoomDamageDeadly = 16;
    public const int PiercingStabsDamageBase = 7;
    public const int PiercingStabsDamageDeadly = 8;
    public const int PiercingStabsRepeat = 2;
    public const int InertiaStrengthBase = 2;
    public const int InertiaStrengthDeadly = 4;
    public const int HardenedShellAmount = 20;

    public SkulkingColony()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int InertiaDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, InertiaDamageDeadly, InertiaDamageBase);

    public static int ZoomDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ZoomDamageDeadly, ZoomDamageBase);

    public static int PiercingStabsDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PiercingStabsDamageDeadly, PiercingStabsDamageBase);

    public static int InertiaStrengthGain(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, InertiaStrengthDeadly, InertiaStrengthBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<HardenedShellPower>(cs, self, HardenedShellAmount, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var zoom = new MoveStateDef(ZoomMoveId, ZoomMove, new SingleAttackIntent(ZoomDamage(cs)));
        var zoom2 = new MoveStateDef(Zoom2MoveId, ZoomMove, new SingleAttackIntent(ZoomDamage(cs)));
        var inertia = new MoveStateDef(InertiaMoveId, InertiaMove, new SingleAttackIntent(InertiaDamage(cs)), Intents.Buff);
        var piercingStabs = new MoveStateDef(PiercingStabsMoveId, PiercingStabsMove, new MultiAttackIntent(PiercingStabsDamage(cs), PiercingStabsRepeat));
        zoom.FollowUpState = zoom2;
        zoom2.FollowUpState = inertia;
        inertia.FollowUpState = piercingStabs;
        piercingStabs.FollowUpState = zoom;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { zoom, zoom2, inertia, piercingStabs }, zoom);
    }

    private static void InertiaMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, InertiaDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, InertiaStrengthGain(cs), self, null);
    }

    private static void PiercingStabsMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PiercingStabsDamage(cs)).WithHitCount(PiercingStabsRepeat).FromMonster(self).Execute();
    }

    private static void ZoomMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ZoomDamage(cs)).FromMonster(self).Execute();
    }
}
