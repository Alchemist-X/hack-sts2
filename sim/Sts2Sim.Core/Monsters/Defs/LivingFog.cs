namespace Sts2Sim.Core.Monsters;

public sealed class LivingFog : MonsterDef
{
    public const string MonsterIdEntry = "LIVING_FOG";
    public const string AdvancedGasMoveId = "ADVANCED_GAS_MOVE";
    public const string BloatMoveId = "BLOAT_MOVE";
    public const string SuperGasBlastMoveId = "SUPER_GAS_BLAST_MOVE";
    public const string GasBombMonsterId = "GAS_BOMB";

    public const int HpBase = 80;
    public const int HpTough = 82;
    public const int AdvancedGasDamageBase = 8;
    public const int AdvancedGasDamageDeadly = 9;
    public const int BloatDamageBase = 5;
    public const int BloatDamageDeadly = 6;
    public const int SuperGasBlastDamageBase = 8;
    public const int SuperGasBlastDamageDeadly = 9;
    public const int SmoggyAmount = 1;
    public const int BloatAmount = 1;

    public LivingFog()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int AdvancedGasDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, AdvancedGasDamageDeadly, AdvancedGasDamageBase);

    public static int BloatDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BloatDamageDeadly, BloatDamageBase);

    public static int SuperGasBlastDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SuperGasBlastDamageDeadly, SuperGasBlastDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var advancedGas = new MoveStateDef(AdvancedGasMoveId, AdvancedGasMove, new SingleAttackIntent(AdvancedGasDamage(cs)), Intents.CardDebuff);
        var bloat = new MoveStateDef(BloatMoveId, BloatMove, new SingleAttackIntent(BloatDamage(cs)), Intents.Summon);
        var superGasBlast = new MoveStateDef(SuperGasBlastMoveId, SuperGasBlastMove, new SingleAttackIntent(SuperGasBlastDamage(cs)));
        advancedGas.FollowUpState = bloat;
        bloat.FollowUpState = superGasBlast;
        superGasBlast.FollowUpState = bloat;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { advancedGas, superGasBlast, bloat }, advancedGas);
    }

    private static void AdvancedGasMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, AdvancedGasDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<SmoggyPower>(cs, targets, SmoggyAmount, self, null);
    }

    private static void BloatMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < BloatAmount; i++)
        {
            string? text = MonsterSpawner.EncounterOf(cs)?.GetNextSlot(cs);
            if (!string.IsNullOrEmpty(text))
            {
                CreatureCmd.Add(cs, MonsterRegistry.Get(GasBombMonsterId), CombatSide.Enemy, text);
            }
        }
        DamageCmd.Attack(cs, BloatDamage(cs)).FromMonster(self).Execute();
    }

    private static void SuperGasBlastMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SuperGasBlastDamage(cs)).FromMonster(self).Execute();
    }
}
