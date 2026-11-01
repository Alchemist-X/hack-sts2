namespace Sts2Sim.Core.Monsters;

public sealed class ChomperExtraState : MonsterExtraState
{
    public bool ScreamFirst { get; set; }

    public override MonsterExtraState DeepClone() => new ChomperExtraState
    {
        ScreamFirst = ScreamFirst
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.ChomperExtra);
        w.WriteBool(ScreamFirst);
    }
}

public sealed class Chomper : MonsterDef
{
    public const string MonsterIdEntry = "CHOMPER";
    public const string ClampMoveId = "CLAMP_MOVE";
    public const string ScreechMoveId = "SCREECH_MOVE";

    public const string DazedCardId = "DAZED";

    public const int MinInitialHpBase = 60;
    public const int MinInitialHpTough = 63;
    public const int MaxInitialHpBase = 64;
    public const int MaxInitialHpTough = 67;
    public const int ClampDamageBase = 8;
    public const int ClampDamageDeadly = 9;
    public const int ClampRepeat = 2;
    public const int ScreechStatusCount = 3;
    public const int SpawnArtifact = 2;

    public Chomper()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ClampDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ClampDamageDeadly, ClampDamageBase);

    public static ChomperExtraState GetExtra(MonsterState monster)
        => monster.Extra as ChomperExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Chomper state");

    public override MonsterExtraState? CreateExtraState() => new ChomperExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<ArtifactPower>(cs, self, SpawnArtifact, self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var clamp = new MoveStateDef(ClampMoveId, ClampMove, new MultiAttackIntent(ClampDamage(cs), ClampRepeat));
        var screech = new MoveStateDef(ScreechMoveId, ScreechMove, new StatusIntent(ScreechStatusCount));
        clamp.FollowUpState = screech;
        screech.FollowUpState = clamp;
        AiStateDef initial = GetExtra(self).ScreamFirst ? screech : clamp;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { clamp, screech }, initial);
    }

    private static void ClampMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ClampDamage(cs)).WithHitCount(ClampRepeat).FromMonster(self).Execute();
    }

    private static void ScreechMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, DazedCardId, PileType.Discard, ScreechStatusCount, null);
    }
}
