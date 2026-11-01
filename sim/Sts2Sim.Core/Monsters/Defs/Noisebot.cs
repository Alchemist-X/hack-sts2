namespace Sts2Sim.Core.Monsters;

public sealed class Noisebot : MonsterDef
{
    public const string MonsterIdEntry = "NOISEBOT";
    public const string NoiseMoveId = "NOISE_MOVE";
    public const string DazedCardId = "DAZED";

    public const int MinInitialHpBase = 18;
    public const int MinInitialHpTough = 19;
    public const int MaxInitialHpBase = 23;
    public const int MaxInitialHpTough = 24;
    public const int NoiseStatusCount = 2;

    public Noisebot()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var noise = new MoveStateDef(NoiseMoveId, NoiseMove, new StatusIntent(NoiseStatusCount));
        noise.FollowUpState = noise;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { noise }, noise);
    }

    private static void NoiseMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            var player = PlayerOf(cs, targets[i]);
            var first = cs.CreateCard(DazedCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, first, PileType.Discard, null);
            var second = cs.CreateCard(DazedCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, second, PileType.Draw, null, CardPilePosition.Random);
        }
    }

    private static PlayerState PlayerOf(CombatState cs, CreatureState target)
    {
        if (target is PlayerState player)
        {
            return player;
        }
        if (target is MonsterState monster && monster.IsPet && cs.GetCreatureById(monster.PetOwnerId) is PlayerState owner)
        {
            return owner;
        }
        throw new InvalidOperationException($"Noisebot target {target.CombatId} has neither a player nor a pet owner");
    }
}
