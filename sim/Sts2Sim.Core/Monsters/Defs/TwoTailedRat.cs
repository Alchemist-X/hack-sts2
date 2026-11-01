namespace Sts2Sim.Core.Monsters;

public sealed class TwoTailedRatExtraState : MonsterExtraState
{
    public int StarterMoveIndex { get; set; } = -1;

    public int TurnsUntilSummonable { get; set; } = TwoTailedRat.InitialTurnsUntilSummonable;

    public int CallForBackupCount { get; set; }

    public override MonsterExtraState DeepClone() => new TwoTailedRatExtraState
    {
        StarterMoveIndex = StarterMoveIndex,
        TurnsUntilSummonable = TurnsUntilSummonable,
        CallForBackupCount = CallForBackupCount
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.TwoTailedRatExtra);
        w.WriteInt32(StarterMoveIndex);
        w.WriteInt32(TurnsUntilSummonable);
        w.WriteInt32(CallForBackupCount);
    }
}

public sealed class TwoTailedRat : MonsterDef
{
    public const string MonsterIdEntry = "TWO_TAILED_RAT";
    public const string ScratchMoveId = "SCRATCH_MOVE";
    public const string DiseaseBiteMoveId = "DISEASE_BITE_MOVE";
    public const string ScreechMoveId = "SCREECH_MOVE";
    public const string CallForBackupMoveId = "CALL_FOR_BACKUP_MOVE";
    public const string RandMoveId = "RAND";

    public const float CallForBackupChance = 0.75f;
    public const int CallForBackupLimit = 3;
    public const int InitialTurnsUntilSummonable = 2;
    public const int MinInitialHpBase = 17;
    public const int MinInitialHpTough = 18;
    public const int MaxInitialHpBase = 21;
    public const int MaxInitialHpTough = 22;
    public const int ScratchDamageBase = 8;
    public const int ScratchDamageDeadly = 9;
    public const int DiseaseBiteDamageBase = 6;
    public const int DiseaseBiteDamageDeadly = 7;
    public const int ScreechFrail = 1;
    public const int ScreechCooldown = 3;
    public const int StarterMoveCount = 3;

    public TwoTailedRat()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int ScratchDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ScratchDamageDeadly, ScratchDamageBase);

    public static int DiseaseBiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DiseaseBiteDamageDeadly, DiseaseBiteDamageBase);

    public static TwoTailedRatExtraState GetExtra(MonsterState monster)
        => monster.Extra as TwoTailedRatExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no TwoTailedRat state");

    public override MonsterExtraState? CreateExtraState() => new TwoTailedRatExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var scratch = new MoveStateDef(ScratchMoveId, ScratchMove, new SingleAttackIntent(ScratchDamage(cs)));
        var diseaseBite = new MoveStateDef(DiseaseBiteMoveId, DiseaseBiteMove, new SingleAttackIntent(DiseaseBiteDamage(cs)));
        var screech = new MoveStateDef(ScreechMoveId, ScreechMove, new DebuffIntent());
        var callForBackup = new MoveStateDef(CallForBackupMoveId, CallForBackup, Intents.Summon);
        var rand = new RandomBranchStateDef(RandMoveId);
        scratch.FollowUpState = rand;
        diseaseBite.FollowUpState = rand;
        screech.FollowUpState = rand;
        callForBackup.FollowUpState = rand;
        rand.AddBranch(scratch, MoveRepeatType.CannotRepeat, static (c, m) => !CanSummon(c, m) ? 1f : (1f / 12f));
        rand.AddBranch(diseaseBite, MoveRepeatType.CannotRepeat, static (c, m) => !CanSummon(c, m) ? 1f : (1f / 12f));
        rand.AddBranch(screech, ScreechCooldown, MoveRepeatType.CannotRepeat, static (c, m) => !CanSummon(c, m) ? 1f : (1f / 12f));
        rand.AddBranch(callForBackup, MoveRepeatType.UseOnlyOnce, static (c, m) => !CanSummon(c, m) ? 0f : CallForBackupChance);
        var states = new AiStateDef[] { rand, scratch, diseaseBite, screech, callForBackup };
        int starter = GetExtra(self).StarterMoveIndex;
        if (starter == -1)
        {
            return new MonsterMoveStateMachineDef(states, rand);
        }
        AiStateDef initial = (starter % StarterMoveCount) switch
        {
            0 => scratch,
            1 => diseaseBite,
            _ => screech
        };
        return new MonsterMoveStateMachineDef(states, initial);
    }

    public static bool CanSummon(CombatState cs, MonsterState self)
    {
        var extra = GetExtra(self);
        if (extra.TurnsUntilSummonable > 0)
        {
            return false;
        }
        if (extra.CallForBackupCount >= CallForBackupLimit)
        {
            return false;
        }
        if (string.IsNullOrEmpty(MonsterSpawner.EncounterOf(cs)?.GetNextSlot(cs)))
        {
            return false;
        }
        var teammates = cs.GetTeammatesOf(self);
        for (int i = 0; i < teammates.Count; i++)
        {
            if (ReferenceEquals(teammates[i], self) || teammates[i] is not MonsterState other)
            {
                continue;
            }
            if (other.HasAi && other.Ai.NextMove != null && other.Ai.NextMove.Id.Equals(CallForBackupMoveId))
            {
                return false;
            }
        }
        return true;
    }

    private static void ScratchMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).TurnsUntilSummonable--;
        DamageCmd.Attack(cs, ScratchDamage(cs)).FromMonster(self).Execute();
    }

    private static void DiseaseBiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).TurnsUntilSummonable--;
        DamageCmd.Attack(cs, DiseaseBiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void ScreechMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).TurnsUntilSummonable--;
        PowerCmd.Apply<FrailPower>(cs, targets, ScreechFrail, self, null);
    }

    private static void CallForBackup(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        var slots = MonsterSpawner.EncounterOf(cs)?.Slots ?? Array.Empty<string>();
        string nextSlot = string.Empty;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (IsSlotFree(cs, slots[i]))
            {
                nextSlot = slots[i];
                break;
            }
        }
        if (!string.IsNullOrEmpty(nextSlot))
        {
            CreatureCmd.Add(cs, MonsterRegistry.Get(MonsterIdEntry), CombatSide.Enemy, nextSlot);
        }
        int maxCallForBackupCount = int.MinValue;
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Def is TwoTailedRat)
            {
                maxCallForBackupCount = Math.Max(maxCallForBackupCount, GetExtra(enemies[i]).CallForBackupCount + 1);
            }
        }
        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i].Def is TwoTailedRat)
            {
                GetExtra(enemies[i]).CallForBackupCount = maxCallForBackupCount;
            }
        }
    }

    private static bool IsSlotFree(CombatState cs, string slot)
    {
        var enemies = cs.Monsters;
        for (int i = 0; i < enemies.Count; i++)
        {
            if (string.Equals(enemies[i].SlotName, slot, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
