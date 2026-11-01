namespace Sts2Sim.Core.Monsters;

public sealed class KnowledgeDemonExtraState : MonsterExtraState
{
    public int CurseOfKnowledgeCounter { get; set; }

    public bool IsBurnt { get; set; }

    public override MonsterExtraState DeepClone() => new KnowledgeDemonExtraState
    {
        CurseOfKnowledgeCounter = CurseOfKnowledgeCounter,
        IsBurnt = IsBurnt
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.KnowledgeDemonExtra);
        w.WriteInt32(CurseOfKnowledgeCounter);
        w.WriteBool(IsBurnt);
    }
}

public sealed class KnowledgeDemon : MonsterDef
{
    public const string MonsterIdEntry = "KNOWLEDGE_DEMON";
    public const string CurseOfKnowledgeMoveId = "CURSE_OF_KNOWLEDGE_MOVE";
    public const string SlapMoveId = "SLAP_MOVE";
    public const string KnowledgeOverwhelmingMoveId = "KNOWLEDGE_OVERWHELMING_MOVE";
    public const string PonderMoveId = "PONDER_MOVE";
    public const string CurseOfKnowledgeBranchId = "CurseOfKnowledgeBranch";
    public const string ChooseCurseMethod = "ChooseCurse";

    public const int HpBase = 379;
    public const int HpTough = 399;
    public const int SlapDamageBase = 17;
    public const int SlapDamageDeadly = 18;
    public const int PonderDamageBase = 11;
    public const int PonderDamageDeadly = 13;
    public const int KnowledgeOverwhelmingDamageBase = 8;
    public const int KnowledgeOverwhelmingDamageDeadly = 9;
    public const int PonderStrengthBase = 2;
    public const int PonderStrengthDeadly = 3;
    public const int KnowledgeOverwhelmingRepeat = 3;
    public const int PonderHeal = 30;
    public const int SinglePlayerCount = 1;
    public const int CurseOfKnowledgeSetCount = 3;

    public static readonly IReadOnlyList<int> DisintegrationDamageValues = new[] { 6, 7, 8 };

    public static readonly IReadOnlyList<IReadOnlyList<string>> CurseOfKnowledgeSets = new IReadOnlyList<string>[]
    {
        new[] { Disintegration.CardId, MindRot.CardId },
        new[] { Disintegration.CardId, Sloth.CardId },
        new[] { Disintegration.CardId, WasteAway.CardId }
    };

    public KnowledgeDemon()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SlapDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SlapDamageDeadly, SlapDamageBase);

    public static int PonderDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PonderDamageDeadly, PonderDamageBase);

    public static int KnowledgeOverwhelmingDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, KnowledgeOverwhelmingDamageDeadly, KnowledgeOverwhelmingDamageBase);

    public static int PonderStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PonderStrengthDeadly, PonderStrengthBase);

    public static KnowledgeDemonExtraState GetExtra(MonsterState monster)
        => monster.Extra as KnowledgeDemonExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no KnowledgeDemon state");

    public override MonsterExtraState? CreateExtraState() => new KnowledgeDemonExtraState();

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var curseOfKnowledge = new MoveStateDef(CurseOfKnowledgeMoveId, CurseOfKnowledgeMove, new DebuffIntent());
        var slap = new MoveStateDef(SlapMoveId, SlapMove, new SingleAttackIntent(SlapDamage(cs)));
        var knowledgeOverwhelming = new MoveStateDef(KnowledgeOverwhelmingMoveId, KnowledgeOverwhelmingMove, new MultiAttackIntent(KnowledgeOverwhelmingDamage(cs), KnowledgeOverwhelmingRepeat));
        var ponder = new MoveStateDef(PonderMoveId, PonderMove, new SingleAttackIntent(PonderDamage(cs)), Intents.Heal, Intents.Buff);
        var branch = new ConditionalBranchStateDef(CurseOfKnowledgeBranchId);
        curseOfKnowledge.FollowUpState = slap;
        slap.FollowUpState = knowledgeOverwhelming;
        knowledgeOverwhelming.FollowUpState = ponder;
        ponder.FollowUpState = branch;
        branch.AddState(curseOfKnowledge, static (_, m) => GetExtra(m).CurseOfKnowledgeCounter < CurseOfKnowledgeSetCount);
        branch.AddState(slap, static (_, m) => GetExtra(m).CurseOfKnowledgeCounter >= CurseOfKnowledgeSetCount);
        return new MonsterMoveStateMachineDef(new AiStateDef[] { branch, curseOfKnowledge, slap, ponder, knowledgeOverwhelming }, curseOfKnowledge);
    }

    private static void CurseOfKnowledgeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var extra = GetExtra(self);
        if (extra.CurseOfKnowledgeCounter >= CurseOfKnowledgeSets.Count)
        {
            throw new InvalidOperationException($"There are no valid sets at this index {extra.CurseOfKnowledgeCounter}");
        }
        for (int i = 0; i < targets.Count; i++)
        {
            ChooseCurse(cs, self, targets[i]);
        }
        if (cs.IsInProgress)
        {
            extra.CurseOfKnowledgeCounter++;
        }
    }

    private static void ChooseCurse(CombatState cs, MonsterState self, CreatureState target)
    {
        if (target.IsDead || target is not PlayerState player)
        {
            return;
        }
        int counter = GetExtra(self).CurseOfKnowledgeCounter;
        int disintegrationDamage = DisintegrationDamageValues[counter];
        var set = CurseOfKnowledgeSets[counter];
        var cards = new List<CardInstance>(set.Count);
        for (int i = 0; i < set.Count; i++)
        {
            var card = cs.CreateCard(set[i], player);
            if (card.Def is Disintegration)
            {
                card.Vars[Disintegration.PowerVarName].BaseValue = disintegrationDamage;
            }
            cards.Add(card);
        }
        var chosen = CardSelectCmd.FromChooseACardScreen(cs, cards, player, false, ChoiceSource.For(MonsterIdEntry, self.CombatId, ChooseCurseMethod));
        if (chosen == null)
        {
            return;
        }
        switch (chosen.Def)
        {
            case Disintegration d:
                d.OnChosen(cs, chosen);
                break;
            case MindRot m:
                m.OnChosen(cs, chosen);
                break;
            case Sloth s:
                s.OnChosen(cs, chosen);
                break;
            case WasteAway w:
                w.OnChosen(cs, chosen);
                break;
        }
    }

    private static void SlapMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, SlapDamage(cs)).FromMonster(self).Execute();
    }

    private static void KnowledgeOverwhelmingMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsBurnt = true;
        DamageCmd.Attack(cs, KnowledgeOverwhelmingDamage(cs)).WithHitCount(KnowledgeOverwhelmingRepeat).FromMonster(self).Execute();
    }

    private static void PonderMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsBurnt = false;
        DamageCmd.Attack(cs, PonderDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.Heal(cs, self, PonderHeal * SinglePlayerCount);
        PowerCmd.Apply<StrengthPower>(cs, self, PonderStrength(cs), self, null);
    }
}
