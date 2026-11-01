namespace Sts2Sim.Core.Monsters;

public sealed class SoulFysh : MonsterDef
{
    public const string MonsterIdEntry = "SOUL_FYSH";
    public const string BeckonMoveId = "BECKON_MOVE";
    public const string DeGasMoveId = "DE_GAS_MOVE";
    public const string GazeMoveId = "GAZE_MOVE";
    public const string FadeMoveId = "FADE_MOVE";
    public const string ScreamMoveId = "SCREAM_MOVE";

    public const string BeckonCardId = "BECKON";

    public const int InitialHpBase = 211;
    public const int InitialHpTough = 221;
    public const int DeGasDamageBase = 16;
    public const int DeGasDamageDeadly = 18;
    public const int ScreamDamageBase = 13;
    public const int ScreamDamageDeadly = 15;
    public const int GazeDamageBase = 7;
    public const int GazeDamageDeadly = 8;
    public const int BeckonMoveAmount = 2;
    public const int GazeMoveAmount = 1;
    public const int ScreamMoveAmount = 3;
    public const int FadeIntangible = 2;

    public SoulFysh()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int DeGasDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, DeGasDamageDeadly, DeGasDamageBase);

    public static int ScreamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, ScreamDamageDeadly, ScreamDamageBase);

    public static int GazeDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GazeDamageDeadly, GazeDamageBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var beckon = new MoveStateDef(BeckonMoveId, BeckonMove, new StatusIntent(BeckonMoveAmount));
        var deGas = new MoveStateDef(DeGasMoveId, DeGasMove, new SingleAttackIntent(DeGasDamage(cs)));
        var gaze = new MoveStateDef(GazeMoveId, GazeMove, new SingleAttackIntent(GazeDamage(cs)), new StatusIntent(GazeMoveAmount));
        var fade = new MoveStateDef(FadeMoveId, FadeMove, Intents.Buff);
        var scream = new MoveStateDef(ScreamMoveId, ScreamMove, new SingleAttackIntent(ScreamDamage(cs)), new DebuffIntent());
        beckon.FollowUpState = deGas;
        deGas.FollowUpState = gaze;
        gaze.FollowUpState = fade;
        fade.FollowUpState = scream;
        scream.FollowUpState = beckon;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { beckon, deGas, gaze, fade, scream }, beckon);
    }

    private static void BeckonMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var player = OwnerPlayer(cs, targets[i]);
            var toDraw = cs.CreateCard(BeckonCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, toDraw, PileType.Draw, null, CardPilePosition.Random);
            var toDiscard = cs.CreateCard(BeckonCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, toDiscard, PileType.Discard, null);
        }
    }

    private static void DeGasMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, DeGasDamage(cs)).FromMonster(self).Execute();
    }

    private static void FadeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<IntangiblePower>(cs, self, FadeIntangible, self, null);
    }

    private static void GazeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GazeDamage(cs)).FromMonster(self).Execute();
        for (int i = 0; i < targets.Count; i++)
        {
            var player = OwnerPlayer(cs, targets[i]);
            var card = cs.CreateCard(BeckonCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, null);
        }
    }

    private static void ScreamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, ScreamDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, targets, ScreamMoveAmount, self, null);
    }

    private static PlayerState OwnerPlayer(CombatState cs, CreatureState creature)
        => creature as PlayerState ?? CombatStatus.PetOwnerOf(cs, creature)
            ?? throw new InvalidOperationException($"Creature {creature.CombatId} is neither a player nor a pet.");
}
