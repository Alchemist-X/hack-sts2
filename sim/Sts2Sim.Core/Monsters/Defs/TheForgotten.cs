namespace Sts2Sim.Core.Monsters;

public sealed class TheForgotten : MonsterDef
{
    public const string MonsterIdEntry = "THE_FORGOTTEN";
    public const string MiasmaMoveId = "MIASMA";
    public const string DreadMoveId = "DREAD";

    public const int InitialHpBase = 106;
    public const int InitialHpTough = 111;
    public const int DreadDamageBase = 13;
    public const int DreadDamageDeadly = 15;
    public const int DexStealAmountBase = 2;
    public const int DexStealAmountDeadly = 2;
    public const int MiasmaBlock = 8;

    public TheForgotten()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int DreadDamage(CombatState cs, MonsterState self)
        => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DreadDamageDeadly, DreadDamageBase) + self.GetPowerAmount<DexterityPower>();

    public static int DebilitatingSmogDexStealAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DexStealAmountDeadly, DexStealAmountBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply(cs, PossessSpeedPower.PowerId, self, 1m, null, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var miasma = new MoveStateDef(MiasmaMoveId, MiasmaMove, new DebuffIntent(), Intents.Defend, Intents.Buff);
        var dread = new MoveStateDef(DreadMoveId, DreadMove, new SingleAttackIntent(static (c, m) => DreadDamage(c, m)));
        miasma.FollowUpState = dread;
        dread.FollowUpState = miasma;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { miasma, dread }, miasma);
    }

    private static void DreadMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DreadDamage(cs, self)).FromMonster(self).Execute();
    }

    private static void MiasmaMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<DexterityPower>(cs, targets, -DebilitatingSmogDexStealAmount(cs), self, null);
        CreatureCmd.GainBlock(cs, self, MiasmaBlock, ValueProp.Move, null);
        PowerCmd.Apply<DexterityPower>(cs, self, DebilitatingSmogDexStealAmount(cs), self, null);
    }
}
