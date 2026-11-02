namespace Sts2Sim.Core.Monsters;

public sealed class MagiKnight : MonsterDef
{
    public const string MonsterIdEntry = "MAGI_KNIGHT";
    public const string PowerShieldMoveId = "POWER_SHIELD_MOVE";
    public const string DampenMoveId = "DAMPEN_MOVE";
    public const string PrepMoveId = "PREP_MOVE";
    public const string MagicBombMoveId = "MAGIC_BOMB";
    public const string RamMoveId = "RAM_MOVE";

    public const int InitialHpBase = 82;
    public const int InitialHpTough = 89;
    public const int PowerShieldDamageBase = 6;
    public const int PowerShieldDamageDeadly = 7;
    public const int PowerShieldBlockBase = 5;
    public const int PowerShieldBlockTough = 9;
    public const int SpearDamageBase = 10;
    public const int SpearDamageDeadly = 11;
    public const int BombDamageBase = 35;
    public const int BombDamageDeadly = 40;
    public const int DampenAmount = 1;

    public MagiKnight()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int PowerShieldDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PowerShieldDamageDeadly, PowerShieldDamageBase);

    public static int PowerShieldBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, PowerShieldBlockTough, PowerShieldBlockBase);

    public static int SpearDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SpearDamageDeadly, SpearDamageBase);

    public static int BombDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BombDamageDeadly, BombDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var powerShield = new MoveStateDef(PowerShieldMoveId, PowerShieldMove, new SingleAttackIntent(PowerShieldDamage(cs)), Intents.Defend);
        var dampen = new MoveStateDef(DampenMoveId, DampenMove, new DebuffIntent());
        var prep = new MoveStateDef(PrepMoveId, PrepMove, Intents.Defend);
        var magicBomb = new MoveStateDef(MagicBombMoveId, MagicBombMove, new SingleAttackIntent(BombDamage(cs)));
        var ram = new MoveStateDef(RamMoveId, SpearMove, new SingleAttackIntent(SpearDamage(cs)));
        powerShield.FollowUpState = dampen;
        dampen.FollowUpState = ram;
        ram.FollowUpState = prep;
        prep.FollowUpState = magicBomb;
        magicBomb.FollowUpState = ram;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { powerShield, dampen, ram, prep, magicBomb }, powerShield);
    }

    private static void DampenMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var current = targets[i];
            var dampen = current.GetPower<DampenPower>();
            bool isNew = dampen == null;
            dampen ??= new DampenPower();
            dampen.AddCaster(self);
            if (!isNew)
            {
                continue;
            }
            PowerCmd.Apply(cs, dampen, current, DampenAmount, self, null);
        }
    }

    private static void MagicBombMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BombDamage(cs)).FromMonster(self).Execute();
    }

    private static void PowerShieldMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PowerShieldDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, PowerShieldBlock(cs), ValueProp.Move, null);
    }

    private static void PrepMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, PowerShieldBlock(cs), ValueProp.Move, null);
    }

    private static void SpearMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SpearDamage(cs)).FromMonster(self).Execute();
    }
}
