namespace Sts2Sim.Core.Monsters;

public sealed class Ovicopter : MonsterDef
{
    public const string MonsterIdEntry = "OVICOPTER";
    public const string LayEggsMoveId = "LAY_EGGS_MOVE";
    public const string SmashMoveId = "SMASH_MOVE";
    public const string TenderizerMoveId = "TENDERIZER_MOVE";
    public const string NutritionalPasteMoveId = "NUTRITIONAL_PASTE_MOVE";
    public const string SummonBranchStateId = "SUMMON_BRANCH_STATE";
    public const string ToughEggMonsterId = "TOUGH_EGG";

    public const int MinInitialHpBase = 124;
    public const int MinInitialHpTough = 126;
    public const int MaxInitialHpBase = 130;
    public const int MaxInitialHpTough = 132;
    public const int SmashDamageBase = 16;
    public const int SmashDamageDeadly = 17;
    public const int TenderizerDamageBase = 7;
    public const int TenderizerDamageDeadly = 8;
    public const int TenderizerVulnerable = 2;
    public const int NutritionalPasteStrengthBase = 3;
    public const int NutritionalPasteStrengthDeadly = 4;
    public const int EggCount = 3;
    public const int MaxLivingTeammatesToLay = 3;

    public Ovicopter()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int SmashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SmashDamageDeadly, SmashDamageBase);

    public static int TenderizerDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, TenderizerDamageDeadly, TenderizerDamageBase);

    public static int NutritionalPasteStrengthAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, NutritionalPasteStrengthDeadly, NutritionalPasteStrengthBase);

    public static bool CanLay(CombatState cs, MonsterState self)
    {
        var teammates = cs.GetTeammatesOf(self);
        int alive = 0;
        for (int i = 0; i < teammates.Count; i++)
        {
            if (teammates[i].IsAlive)
            {
                alive++;
            }
        }
        return alive <= MaxLivingTeammatesToLay;
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var layEggs = new MoveStateDef(LayEggsMoveId, LayEggsMove, Intents.Summon);
        var smash = new MoveStateDef(SmashMoveId, SmashMove, new SingleAttackIntent(SmashDamage(cs)));
        var tenderizer = new MoveStateDef(TenderizerMoveId, TenderizerMove, new SingleAttackIntent(TenderizerDamage(cs)), new DebuffIntent());
        var nutritionalPaste = new MoveStateDef(NutritionalPasteMoveId, NutritionalPasteMove, Intents.Buff);
        var summonBranch = new ConditionalBranchStateDef(SummonBranchStateId);
        layEggs.FollowUpState = smash;
        nutritionalPaste.FollowUpState = smash;
        smash.FollowUpState = tenderizer;
        tenderizer.FollowUpState = summonBranch;
        summonBranch.AddState(layEggs, static (c, m) => CanLay(c, m));
        summonBranch.AddState(nutritionalPaste, static (c, m) => !CanLay(c, m));
        return new MonsterMoveStateMachineDef(new AiStateDef[] { nutritionalPaste, layEggs, smash, tenderizer, summonBranch }, layEggs);
    }

    private static void LayEggsMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < EggCount; i++)
        {
            var encounter = MonsterSpawner.EncounterOf(cs);
            string? text = encounter != null ? LastFreeSlot(cs, encounter.Slots) : null;
            if (text != null)
            {
                var egg = CreatureCmd.Add(cs, MonsterRegistry.Get(ToughEggMonsterId), CombatSide.Enemy, text);
                PowerCmd.Apply<MinionPower>(cs, egg, 1, self, null);
            }
        }
    }

    private static string? LastFreeSlot(CombatState cs, IReadOnlyList<string> slots)
    {
        var enemies = cs.Monsters;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            bool free = true;
            for (int j = 0; j < enemies.Count; j++)
            {
                if (string.Equals(enemies[j].SlotName, slots[i], StringComparison.Ordinal))
                {
                    free = false;
                    break;
                }
            }
            if (free)
            {
                return slots[i];
            }
        }
        return null;
    }

    private static void NutritionalPasteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, NutritionalPasteStrengthAmount(cs), self, null);
    }

    private static void SmashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SmashDamage(cs)).FromMonster(self).Execute();
    }

    private static void TenderizerMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, TenderizerDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, targets, TenderizerVulnerable, self, null);
    }
}
