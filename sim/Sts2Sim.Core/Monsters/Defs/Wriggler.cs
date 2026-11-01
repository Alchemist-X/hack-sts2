namespace Sts2Sim.Core.Monsters;

public sealed class WrigglerExtraState : MonsterExtraState
{
    public bool StartStunned { get; set; }

    public override MonsterExtraState DeepClone() => new WrigglerExtraState
    {
        StartStunned = StartStunned
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.WrigglerExtra);
        w.WriteBool(StartStunned);
    }
}

public sealed class Wriggler : MonsterDef
{
    public const string MonsterIdEntry = "WRIGGLER";
    public const string NastyBiteMoveId = "NASTY_BITE_MOVE";
    public const string WriggleMoveId = "WRIGGLE_MOVE";
    public const string SpawnedMoveId = "SPAWNED_MOVE";
    public const string InitMoveId = "INIT_MOVE";

    public const string InfectionCardId = "INFECTION";
    public const string Slot1 = "wriggler1";
    public const string Slot2 = "wriggler2";
    public const string Slot3 = "wriggler3";
    public const string Slot4 = "wriggler4";

    public const int MinInitialHpBase = 17;
    public const int MinInitialHpTough = 18;
    public const int MaxInitialHpBase = 21;
    public const int MaxInitialHpTough = 22;
    public const int BiteDamageBase = 6;
    public const int BiteDamageDeadly = 7;
    public const int WriggleStrength = 2;
    public const int WriggleInfection = 1;

    public Wriggler()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static WrigglerExtraState GetExtra(MonsterState monster)
        => monster.Extra as WrigglerExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Wriggler state");

    public override MonsterExtraState? CreateExtraState() => new WrigglerExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var bite = new MoveStateDef(NastyBiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var wriggle = new MoveStateDef(WriggleMoveId, WriggleMove, Intents.Buff, new StatusIntent(WriggleInfection));
        var spawned = new MoveStateDef(SpawnedMoveId, SpawnedMove, Intents.Stun);
        var init = new ConditionalBranchStateDef(InitMoveId);
        init.AddState(bite, static (_, m) => m.SlotName == Slot1);
        init.AddState(wriggle, static (_, m) => m.SlotName == Slot2);
        init.AddState(bite, static (_, m) => m.SlotName == Slot3);
        init.AddState(wriggle, static (_, m) => m.SlotName == Slot4);
        spawned.FollowUpState = init;
        bite.FollowUpState = wriggle;
        wriggle.FollowUpState = bite;
        AiStateDef initial = GetExtra(self).StartStunned ? spawned : init;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { spawned, bite, wriggle, init }, initial);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void WriggleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CardPileCmd.AddToCombatAndPreview(cs, targets, InfectionCardId, PileType.Discard, WriggleInfection, null);
        PowerCmd.Apply<StrengthPower>(cs, self, WriggleStrength, self, null);
    }

    private static void SpawnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
