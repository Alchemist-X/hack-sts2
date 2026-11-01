namespace Sts2Sim.Core.Monsters;

public sealed class GremlinMerc : MonsterDef
{
    public const string MonsterIdEntry = "GREMLIN_MERC";
    public const string GimmeMoveId = "GIMME_MOVE";
    public const string DoubleSmashMoveId = "DOUBLE_SMASH_MOVE";
    public const string HeheMoveId = "HEHE_MOVE";
    public const string SurprisePowerId = "SURPRISE_POWER";
    public const string ThieveryPowerId = "THIEVERY_POWER";

    public const int MinInitialHpBase = 47;
    public const int MinInitialHpTough = 51;
    public const int MaxInitialHpBase = 49;
    public const int MaxInitialHpTough = 53;
    public const int GimmeDamageBase = 7;
    public const int GimmeDamageTough = 8;
    public const int GimmeRepeat = 2;
    public const int DoubleSmashDamageBase = 6;
    public const int DoubleSmashDamageTough = 7;
    public const int DoubleSmashRepeat = 2;
    public const int HeheDamageBase = 8;
    public const int HeheDamageTough = 9;
    public const decimal SurpriseAmount = 1m;
    public const decimal ThieveryAmount = 20m;
    public const decimal DoubleSmashWeak = 2m;
    public const decimal HeheStrength = 2m;

    public GremlinMerc()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int GimmeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, GimmeDamageTough, GimmeDamageBase);

    public static int DoubleSmashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, DoubleSmashDamageTough, DoubleSmashDamageBase);

    public static int HeheDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HeheDamageTough, HeheDamageBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<SurprisePower>(cs, self, SurpriseAmount, self, null);
        foreach (var player in cs.Run.Players.ToList())
        {
            var thieveryPower = new ThieveryPower
            {
                TargetId = player.CombatId
            };
            PowerCmd.Apply(cs, thieveryPower, self, ThieveryAmount, self, null);
        }
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var gimme = new MoveStateDef(GimmeMoveId, GimmeMove, new MultiAttackIntent(GimmeDamage(cs), GimmeRepeat));
        var doubleSmash = new MoveStateDef(DoubleSmashMoveId, DoubleSmashMove, new MultiAttackIntent(DoubleSmashDamage(cs), DoubleSmashRepeat), new DebuffIntent());
        var hehe = new MoveStateDef(HeheMoveId, HeheMove, new SingleAttackIntent(HeheDamage(cs)), Intents.Buff);
        gimme.FollowUpState = doubleSmash;
        doubleSmash.FollowUpState = hehe;
        hehe.FollowUpState = gimme;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { gimme, doubleSmash, hehe }, gimme);
    }

    private static void GimmeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GimmeDamage(cs)).WithHitCount(GimmeRepeat).FromMonster(self).Execute();
        StealAll(cs, self);
    }

    private static void DoubleSmashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DoubleSmashDamage(cs)).WithHitCount(DoubleSmashRepeat).FromMonster(self).Execute();
        StealAll(cs, self);
        PowerCmd.Apply<WeakPower>(cs, targets, DoubleSmashWeak, self, null);
    }

    private static void HeheMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, HeheDamage(cs)).FromMonster(self).Execute();
        StealAll(cs, self);
        PowerCmd.Apply<StrengthPower>(cs, self, HeheStrength, self, null);
    }

    private static void StealAll(CombatState cs, MonsterState self)
    {
        foreach (var current in self.Powers.OfType<ThieveryPower>().ToList())
        {
            current.Steal(cs);
        }
    }
}
