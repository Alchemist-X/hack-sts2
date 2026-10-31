namespace Sts2Sim.Core.Monsters;

public sealed class CorpseSlugExtraState : MonsterExtraState
{
    public const ushort CanonicalTag = 0x4D41;

    public int StarterMoveIdx { get; set; }

    public bool IsRavenous { get; set; }

    public override MonsterExtraState DeepClone() => new CorpseSlugExtraState
    {
        StarterMoveIdx = StarterMoveIdx,
        IsRavenous = IsRavenous
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CanonicalTag);
        w.WriteInt32(StarterMoveIdx);
        w.WriteBool(IsRavenous);
    }
}

public sealed class CorpseSlug : MonsterDef
{
    public const string MonsterIdEntry = "CORPSE_SLUG";
    public const string WhipSlapMoveId = "WHIP_SLAP_MOVE";
    public const string GlompMoveId = "GLOMP_MOVE";
    public const string GoopMoveId = "GOOP_MOVE";

    public const int MinInitialHpBase = 25;
    public const int MinInitialHpTough = 27;
    public const int MaxInitialHpBase = 27;
    public const int MaxInitialHpTough = 29;
    public const int WhipSlapDamage = 3;
    public const int WhipSlapRepeat = 2;
    public const int GlompDamageBase = 8;
    public const int GlompDamageDeadly = 9;
    public const int GoopFrailAmount = 2;
    public const int RavenousStrengthBase = 4;
    public const int RavenousStrengthDeadly = 5;
    public const int StarterMoveCount = 3;

    public CorpseSlug()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int GlompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GlompDamageDeadly, GlompDamageBase);

    public static int RavenousStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RavenousStrengthDeadly, RavenousStrengthBase);

    public static CorpseSlugExtraState GetExtra(MonsterState monster)
        => monster.Extra as CorpseSlugExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no CorpseSlug state");

    public override MonsterExtraState? CreateExtraState() => new CorpseSlugExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<RavenousPower>(cs, self, RavenousStrength(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var whipSlap = new MoveStateDef(WhipSlapMoveId, WhipSlapMove, new MultiAttackIntent(WhipSlapDamage, WhipSlapRepeat));
        var glomp = new MoveStateDef(GlompMoveId, GlompMove, new SingleAttackIntent(GlompDamage(cs)));
        var goop = new MoveStateDef(GoopMoveId, GoopMove, new DebuffIntent());
        whipSlap.FollowUpState = glomp;
        glomp.FollowUpState = goop;
        goop.FollowUpState = whipSlap;
        AiStateDef initial = (GetExtra(self).StarterMoveIdx % StarterMoveCount) switch
        {
            0 => whipSlap,
            1 => glomp,
            _ => goop
        };
        return new MonsterMoveStateMachineDef(new AiStateDef[] { whipSlap, glomp, goop }, initial);
    }

    public static MonsterSpawn[] EnsureCorpseSlugsStartWithDifferentMoves(IReadOnlyList<MonsterSpawn> spawns, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(spawns);
        ArgumentNullException.ThrowIfNull(rng);
        int num = rng.NextInt(StarterMoveCount);
        var result = new MonsterSpawn[spawns.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var spawn = spawns[i];
            if (spawn.Def is not CorpseSlug)
            {
                result[i] = spawn;
                continue;
            }
            int starterMoveIdx = num % StarterMoveCount;
            var configure = spawn.Configure;
            result[i] = spawn with
            {
                Configure = m =>
                {
                    configure?.Invoke(m);
                    GetExtra(m).StarterMoveIdx = starterMoveIdx;
                }
            };
            num++;
        }
        return result;
    }

    private static void WhipSlapMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WhipSlapDamage).WithHitCount(WhipSlapRepeat).FromMonster(self).Execute();
    }

    private static void GlompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GlompDamage(cs)).FromMonster(self).Execute();
    }

    private static void GoopMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<FrailPower>(cs, targets, GoopFrailAmount, self, null);
    }
}
