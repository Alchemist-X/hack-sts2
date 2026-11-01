namespace Sts2Sim.Core.Monsters;

public sealed class TrackerRubyRaider : MonsterDef
{
    public const string MonsterIdEntry = "TRACKER_RUBY_RAIDER";
    public const string TrackMoveId = "TRACK_MOVE";
    public const string HoundsMoveId = "HOUNDS_MOVE";

    public const int MinInitialHpBase = 21;
    public const int MinInitialHpTough = 22;
    public const int MaxInitialHpBase = 25;
    public const int MaxInitialHpTough = 26;
    public const int HoundsDamageBase = 1;
    public const int HoundsDamageDeadly = 1;
    public const int HoundsRepeatBase = 8;
    public const int HoundsRepeatDeadly = 9;
    public const int TrackFrail = 2;

    public TrackerRubyRaider()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int HoundsDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HoundsDamageDeadly, HoundsDamageBase);

    public static int HoundsRepeat(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HoundsRepeatDeadly, HoundsRepeatBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var track = new MoveStateDef(TrackMoveId, TrackMove, new DebuffIntent());
        var hounds = new MoveStateDef(HoundsMoveId, HoundsMove, new MultiAttackIntent(HoundsDamage(cs), HoundsRepeat(cs)));
        track.FollowUpState = hounds;
        hounds.FollowUpState = hounds;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { track, hounds }, track);
    }

    private static void HoundsMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HoundsDamage(cs)).WithHitCount(HoundsRepeat(cs)).FromMonster(self).Execute();
    }

    private static void TrackMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<FrailPower>(cs, targets, TrackFrail, self, null);
    }
}
