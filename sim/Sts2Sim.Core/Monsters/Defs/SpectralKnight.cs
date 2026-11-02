namespace Sts2Sim.Core.Monsters;

public sealed class SpectralKnight : MonsterDef
{
    public const string MonsterIdEntry = "SPECTRAL_KNIGHT";
    public const string HexMoveId = "HEX";
    public const string SoulSlashMoveId = "SOUL_SLASH";
    public const string SoulFlameMoveId = "SOUL_FLAME";
    public const string RandMoveId = "RAND";

    public const int HpBase = 93;
    public const int HpTough = 97;
    public const int SoulSlashDamageBase = 15;
    public const int SoulSlashDamageDeadly = 17;
    public const int SoulFlameDamageBase = 3;
    public const int SoulFlameDamageDeadly = 4;
    public const int SoulFlameRepeat = 3;
    public const int HexAmount = 2;

    public SpectralKnight()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SoulSlashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SoulSlashDamageDeadly, SoulSlashDamageBase);

    public static int SoulFlameDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SoulFlameDamageDeadly, SoulFlameDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var hex = new MoveStateDef(HexMoveId, HexMove, new DebuffIntent());
        var soulSlash = new MoveStateDef(SoulSlashMoveId, SoulSlashMove, new SingleAttackIntent(SoulSlashDamage(cs)));
        var soulFlame = new MoveStateDef(SoulFlameMoveId, SoulFlameMove, new MultiAttackIntent(SoulFlameDamage(cs), SoulFlameRepeat));
        var rand = new RandomBranchStateDef(RandMoveId);
        hex.FollowUpState = soulSlash;
        soulSlash.FollowUpState = rand;
        soulFlame.FollowUpState = rand;
        rand.AddBranch(soulSlash, 2);
        rand.AddBranch(soulFlame, MoveRepeatType.CannotRepeat);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { hex, soulSlash, soulFlame, rand }, hex);
    }

    private static void HexMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            PowerCmd.Apply<HexPower>(cs, targets[i], HexAmount, self, null);
        }
    }

    private static void SoulSlashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SoulSlashDamage(cs)).FromMonster(self).Execute();
    }

    private static void SoulFlameMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SoulFlameDamage(cs)).WithHitCount(SoulFlameRepeat).FromMonster(self).Execute();
    }
}
