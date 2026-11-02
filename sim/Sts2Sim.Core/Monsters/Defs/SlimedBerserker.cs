namespace Sts2Sim.Core.Monsters;

public sealed class SlimedBerserker : MonsterDef
{
    public const string MonsterIdEntry = "SLIMED_BERSERKER";
    public const string VomitIchorMoveId = "VOMIT_ICHOR_MOVE";
    public const string LeechingHugMoveId = "LEECHING_HUG_MOVE";
    public const string SmotherMoveId = "SMOTHER_MOVE";
    public const string FuriousPummelingMoveId = "FURIOUS_PUMMELING_MOVE";

    public const int InitialHpBase = 261;
    public const int InitialHpTough = 281;
    public const int PummelingDamageBase = 4;
    public const int PummelingDamageDeadly = 5;
    public const int PummelingRepeat = 4;
    public const int SmotherDamageBase = 30;
    public const int SmotherDamageDeadly = 33;
    public const int LeechingDrain = 3;
    public const int VomitSlimeInDiscard = 10;

    public SlimedBerserker()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int PummelingDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PummelingDamageDeadly, PummelingDamageBase);

    public static int SmotherDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SmotherDamageDeadly, SmotherDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var vomitIchor = new MoveStateDef(VomitIchorMoveId, VomitIchorMove, new StatusIntent(VomitSlimeInDiscard));
        var leechingHug = new MoveStateDef(LeechingHugMoveId, LeechingHugMove, new DebuffIntent(), Intents.Buff);
        var smother = new MoveStateDef(SmotherMoveId, SmotherMove, new SingleAttackIntent(SmotherDamage(cs)));
        var furiousPummeling = new MoveStateDef(FuriousPummelingMoveId, FuriousPummelingMove, new MultiAttackIntent(PummelingDamage(cs), PummelingRepeat));
        vomitIchor.FollowUpState = furiousPummeling;
        furiousPummeling.FollowUpState = leechingHug;
        leechingHug.FollowUpState = smother;
        smother.FollowUpState = vomitIchor;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { vomitIchor, smother, leechingHug, furiousPummeling }, vomitIchor);
    }

    private static void VomitIchorMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, VomitSlimeInDiscard, null);
    }

    private static void LeechingHugMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<WeakPower>(cs, targets, LeechingDrain, null, null);
        PowerCmd.Apply<StrengthPower>(cs, self, LeechingDrain, self, null);
    }

    private static void FuriousPummelingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PummelingDamage(cs)).WithHitCount(PummelingRepeat).FromMonster(self).Execute();
    }

    private static void SmotherMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SmotherDamage(cs)).FromMonster(self).Execute();
    }
}
