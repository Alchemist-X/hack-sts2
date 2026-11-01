using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_2_1Tests
{
    private const string PowersArea = "W3 powers owners (wave A)";

    private static readonly string[] PortedIds =
    {
        "ACCELERANT", "AFTERIMAGE", "ANTICIPATE", "APPARITION", "ARSENAL", "AUTOMATION", "BARRICADE",
        "BATTLE_TRANCE", "BLIGHT_STRIKE", "BLUR", "BOOST_AWAY", "BORROWED_TIME", "BOUNCING_FLASK",
        "BUBBLE_BUBBLE", "BUFFER", "BULLET_TIME", "BURST", "CHARGE_BATTERY", "COLOSSUS", "CORROSIVE_WAVE",
        "COUNTDOWN", "CRIMSON_MANTLE", "CRUELTY", "CRUSH_UNDER", "DANSE_MACABRE", "DARK_EMBRACE",
        "DARK_SHACKLES", "DEADLY_POISON", "DEATHBRINGER", "DEBILITATE", "DELAY", "DEMESNE", "DEMON_FORM",
        "DODGE_AND_ROLL", "ENFEEBLING_TOUCH", "ENVENOM"
    };

    private static readonly Cards21Dummy DummyDef = new();

    public static void Test_Content_cards_2_1_AllPortedIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.Defs.Contains(id), id);
            var def = CardRegistry.Get(id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name), id);
            Assert.True(def.CreateVars() != null, id);
        }
    }

    public static void Test_Content_cards_2_1_BlightStrikeAppliesDoomEqualToDamageDealt()
    {
        W1Pending.Run(nameof(Test_Content_cards_2_1_BlightStrikeAppliesDoomEqualToDamageDealt), PowersArea, () =>
        {
            RequirePowers(BlightStrike.DoomPowerId);
            var cs = NewCombat();
            var monster = AddMonster(cs, 40);
            monster.Block = 3;
            var card = MakeIn(cs, PileType.Play, BlightStrike.CardId);
            card.OnPlay(cs, new CardPlay(cs, card, monster));
            Assert.Equal(40 - (int)BlightStrike.BaseDamage + 3, monster.CurrentHp);
            Assert.Equal((int)BlightStrike.BaseDamage, monster.GetPower(BlightStrike.DoomPowerId)!.Amount, "blocked damage counts toward Doom");
        });
    }

    public static void Test_Content_cards_2_1_BulletTimeFreesNonXCardsAndAppliesNoDraw()
    {
        W1Pending.Run(nameof(Test_Content_cards_2_1_BulletTimeFreesNonXCardsAndAppliesNoDraw), PowersArea, () =>
        {
            RequirePowers(BulletTime.NoDrawPowerId);
            var cs = NewCombat();
            AddMonster(cs, 40);
            var inHand = MakeIn(cs, PileType.Hand, Armaments.CardId);
            var card = MakeIn(cs, PileType.Play, BulletTime.CardId);
            card.OnPlay(cs, new CardPlay(cs, card, null));
            Assert.Equal(0, inHand.EnergyCost.GetAmountToSpend(cs, inHand));
            Assert.True(cs.Player.HasPower(BulletTime.NoDrawPowerId));
        });
    }

    public static void Test_Content_cards_2_1_DodgeAndRollCarriesTheBlockGained()
    {
        W1Pending.Run(nameof(Test_Content_cards_2_1_DodgeAndRollCarriesTheBlockGained), PowersArea, () =>
        {
            RequirePowers(DodgeAndRoll.BlockNextTurnPowerId);
            var cs = NewCombat();
            AddMonster(cs, 40);
            var card = MakeIn(cs, PileType.Play, DodgeAndRoll.CardId);
            card.OnPlay(cs, new CardPlay(cs, card, null));
            Assert.Equal((int)DodgeAndRoll.BaseBlock, cs.Player.Block);
            Assert.Equal((int)DodgeAndRoll.BaseBlock, cs.Player.GetPower(DodgeAndRoll.BlockNextTurnPowerId)!.Amount);
        });
    }

    private static void RequirePowers(params string[] ids)
    {
        foreach (var id in ids)
        {
            if (!PowerRegistry.AllIds.Contains(id))
            {
                throw new NotImplementedException($"power {id} not ported yet");
            }
        }
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = 3
        };
        var cs = new CombatState(player, "CARDS_2_1_TESTS", 0, new RunRngSet(7UL));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyDef, hp, hp, null, null);
        var idle = new MoveStateDef("CARDS_2_1_TESTS_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private static CardInstance MakeIn(CombatState cs, PileType pile, string id)
    {
        var card = cs.CreateCard(CardRegistry.Get(id), cs.Player);
        cs.Player.GetPile(pile).AddInternal(card);
        return card;
    }

    private sealed class Cards21Dummy : MonsterDef
    {
        public Cards21Dummy()
            : base("CARDS_2_1_TESTS_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
