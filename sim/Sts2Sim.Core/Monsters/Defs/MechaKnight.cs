namespace Sts2Sim.Core.Monsters;

public sealed class MechaKnight : MonsterDef
{
    public const string MonsterIdEntry = "MECHA_KNIGHT";
    public const string ChargeMoveId = "CHARGE_MOVE";
    public const string FlamethrowerMoveId = "FLAMETHROWER_MOVE";
    public const string WindupMoveId = "WINDUP_MOVE";
    public const string HeavyCleaveMoveId = "HEAVY_CLEAVE_MOVE";

    public const string BurnCardId = "BURN";

    public const int InitialHpBase = 300;
    public const int InitialHpTough = 320;
    public const int ChargeDamageBase = 25;
    public const int ChargeDamageDeadly = 30;
    public const int FlamethrowerDamageBase = 8;
    public const int FlamethrowerDamageDeadly = 12;
    public const int HeavyCleaveDamageBase = 35;
    public const int HeavyCleaveDamageDeadly = 40;
    public const int FlamethrowerCardCount = 4;
    public const int WindupBlock = 15;
    public const int WindupStrength = 5;
    public const int SpawnArtifact = 3;

    public MechaKnight()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ChargeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ChargeDamageDeadly, ChargeDamageBase);

    public static int FlamethrowerDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, FlamethrowerDamageDeadly, FlamethrowerDamageBase);

    public static int HeavyCleaveDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, HeavyCleaveDamageDeadly, HeavyCleaveDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<ArtifactPower>(cs, self, SpawnArtifact, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var charge = new MoveStateDef(ChargeMoveId, ChargeMove, new SingleAttackIntent(ChargeDamage(cs)));
        var flamethrower = new MoveStateDef(FlamethrowerMoveId, FlamethrowerMove, new SingleAttackIntent(FlamethrowerDamage(cs)), new StatusIntent(FlamethrowerCardCount));
        var windup = new MoveStateDef(WindupMoveId, WindupMove, Intents.Defend, Intents.Buff);
        var heavyCleave = new MoveStateDef(HeavyCleaveMoveId, HeavyCleaveMove, new SingleAttackIntent(HeavyCleaveDamage(cs)));
        charge.FollowUpState = flamethrower;
        flamethrower.FollowUpState = windup;
        windup.FollowUpState = heavyCleave;
        heavyCleave.FollowUpState = flamethrower;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { charge, flamethrower, windup, heavyCleave }, charge);
    }

    private static void ChargeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ChargeDamage(cs)).FromMonster(self).Execute();
    }

    private static void FlamethrowerMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, FlamethrowerDamage(cs)).FromMonster(self).Execute();
        CardPileCmd.AddToCombatAndPreview(cs, targets, BurnCardId, PileType.Hand, FlamethrowerCardCount, null);
    }

    private static void HeavyCleaveMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HeavyCleaveDamage(cs)).FromMonster(self).Execute();
    }

    private static void WindupMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, WindupBlock, ValueProp.Move, null);
        PowerCmd.Apply<StrengthPower>(cs, self, WindupStrength, self, null);
    }
}
