namespace Sts2Sim.Core.Monsters;

public sealed class SnappingJaxfruit : MonsterDef
{
    public const string MonsterIdEntry = "SNAPPING_JAXFRUIT";
    public const string EnergyOrbMoveId = "ENERGY_ORB_MOVE";

    public const int MinInitialHpBase = 31;
    public const int MinInitialHpTough = 34;
    public const int MaxInitialHpBase = 33;
    public const int MaxInitialHpTough = 36;
    public const int EnergyDamageBase = 3;
    public const int EnergyDamageDeadly = 4;
    public const int EnergyOrbStrength = 2;

    public SnappingJaxfruit()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int EnergyDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EnergyDamageDeadly, EnergyDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var energyOrb = new MoveStateDef(EnergyOrbMoveId, EnergyOrbMove, new SingleAttackIntent(EnergyDamage(cs)), Intents.Buff);
        energyOrb.FollowUpState = energyOrb;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { energyOrb }, energyOrb);
    }

    private static void EnergyOrbMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, EnergyDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<StrengthPower>(cs, self, EnergyOrbStrength, self, null);
    }
}
