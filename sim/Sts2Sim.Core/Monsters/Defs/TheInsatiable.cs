namespace Sts2Sim.Core.Monsters;

public sealed class TheInsatiable : MonsterDef
{
    public const string MonsterIdEntry = "THE_INSATIABLE";
    public const string LiquifyGroundMoveId = "LIQUIFY_GROUND_MOVE";
    public const string ThrashMoveId = "THRASH_MOVE";
    public const string Thrash2MoveId = "THRASH_MOVE_2";
    public const string LungingBiteMoveId = "LUNGING_BITE_MOVE";
    public const string SalivateMoveId = "SALIVATE_MOVE";

    public const string FranticEscapeCardId = "FRANTIC_ESCAPE";

    public const int InitialHpBase = 321;
    public const int InitialHpTough = 341;
    public const int ThrashDamageBase = 8;
    public const int ThrashDamageDeadly = 9;
    public const int BiteDamageBase = 28;
    public const int BiteDamageDeadly = 31;
    public const int SalivateStrengthBase = 2;
    public const int SalivateStrengthDeadly = 3;
    public const int LiquifyStatusDrawCount = 3;
    public const int LiquifyStatusDiscardCount = 3;
    public const int LiquifyStatusIntentCount = 6;
    public const int ThrashRepeat = 2;
    public const int SandpitAmount = 4;

    public TheInsatiable()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int ThrashDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ThrashDamageDeadly, ThrashDamageBase);

    public static int BiteDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BiteDamageDeadly, BiteDamageBase);

    public static int SalivateStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, SalivateStrengthDeadly, SalivateStrengthBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var liquify = new MoveStateDef(LiquifyGroundMoveId, LiquifyMove, Intents.Buff, new StatusIntent(LiquifyStatusIntentCount));
        var thrash = new MoveStateDef(ThrashMoveId, ThrashMove, new MultiAttackIntent(ThrashDamage(cs), ThrashRepeat));
        var thrash2 = new MoveStateDef(Thrash2MoveId, ThrashMove, new MultiAttackIntent(ThrashDamage(cs), ThrashRepeat));
        var bite = new MoveStateDef(LungingBiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage(cs)));
        var salivate = new MoveStateDef(SalivateMoveId, SalivateMove, Intents.Buff);
        liquify.FollowUpState = thrash;
        thrash.FollowUpState = bite;
        bite.FollowUpState = salivate;
        salivate.FollowUpState = thrash2;
        thrash2.FollowUpState = thrash;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { liquify, thrash, thrash2, bite, salivate }, liquify);
    }

    private static void BiteMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, BiteDamage(cs)).FromMonster(self).Execute();
    }

    private static void LiquifyMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var sandpit = new SandpitPower { TargetId = targets[i].CombatId };
            PowerCmd.Apply(cs, sandpit, self, SandpitAmount, self, null);
        }
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var player = target as PlayerState ?? CombatStatus.PetOwnerOf(cs, target)
                ?? throw new InvalidOperationException($"Creature {target.CombatId} is neither a player nor a pet.");
            for (int j = 0; j < LiquifyStatusDrawCount + LiquifyStatusDiscardCount; j++)
            {
                var card = cs.CreateCard(FranticEscapeCardId, player);
                var pile = j < LiquifyStatusDrawCount ? PileType.Draw : PileType.Discard;
                CardPileCmd.AddGeneratedCardToCombat(cs, card, pile, null, CardPilePosition.Random);
            }
        }
    }

    private static void SalivateMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<StrengthPower>(cs, self, SalivateStrength(cs), self, null);
    }

    private static void ThrashMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ThrashDamage(cs)).WithHitCount(ThrashRepeat).FromMonster(self).Execute();
    }
}
