using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_5_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ACROBATICS", "ARMAMENTS", "BEGONE", "BRAND", "BURNING_PACT", "CHARGE", "COSMIC_INDIFFERENCE",
        "DAGGER_THROW", "DREDGE", "DUAL_WIELD", "ENTROPY", "FOREGONE_CONCLUSION", "GLIMMER", "GRAVEBLAST",
        "GUARDS", "HAND_TRICK", "HEADBUTT", "HEIRLOOM_HAMMER", "HIDDEN_DAGGERS", "HOLOGRAM", "NEOWS_FURY",
        "NIGHTMARE", "PHOTON_CUT", "PREPARED", "PURITY", "SCAVENGE", "SCULPTING_STRIKE", "SEANCE",
        "SECRET_TECHNIQUE", "SECRET_WEAPON", "SEEKER_STRIKE", "STRATAGEM", "SURVIVOR", "THINKING_AHEAD",
        "TOOLS_OF_THE_TRADE", "TRANSFIGURE", "TRUE_GRIT", "TYRANNY", "WISH"
    };

    public static void Test_Content_cards_5_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.Defs.Contains(id), id);
            var def = CardRegistry.Get(id);
            Assert.Equal(id, def.Id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name));
            Assert.True(def is not UnportedCardDef, id);
        }
        Assert.True(CardRegistry.Get("DUAL_WIELD").VisualCardPool.IsColorless == false);
        Assert.True(CardRegistry.Get("WISH").VisualCardPool.IsColorless);
    }

    public static void Test_Content_cards_5_1_TrueGritUnupgradedExhaustsRandomHandCard()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, "CARDS_5_1_TESTS", 0, new RunRngSet(new Rng(7UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        var monster = new MonsterState(cs.AllocateCreatureId(), new DummyMonster(), 40, 40, null, null);
        var idle = new MoveStateDef("CARDS_5_1_TESTS_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        var trueGrit = cs.CreateCard("TRUE_GRIT", player);
        player.GetPile(PileType.Play).AddInternal(trueGrit);
        var other = cs.CreateCard("STRIKE_IRONCLAD", player);
        player.GetPile(PileType.Hand).AddInternal(other);
        trueGrit.OnPlay(cs, new CardPlay(cs, trueGrit, null));
        Assert.Equal(7, player.Block);
        Assert.Equal(0, player.Hand.Cards.Count);
        Assert.True(player.ExhaustPile.Cards.Contains(other));
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CARDS_5_1_TESTS_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
