using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_1_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ANCHOR", "BAG_OF_MARBLES", "BAG_OF_PREPARATION", "BEATING_REMNANT", "BLOOD_VIAL", "BREAD",
        "BRILLIANT_SCARF", "BRIMSTONE", "CANDELABRA", "CAPTAINS_WHEEL", "CHANDELIER", "CHARONS_ASHES",
        "CHEMICAL_X", "CIRCLET", "CLOAK_CLASP", "DAUGHTER_OF_THE_WIND", "DEMON_TONGUE", "FAKE_ANCHOR",
        "FAKE_BLOOD_VIAL", "FAKE_HAPPY_FLOWER", "FAKE_MERCHANTS_RUG", "FAKE_ORICHALCUM", "FAKE_STRIKE_DUMMY",
        "FESTIVE_POPPER", "FIDDLE", "FORGOTTEN_SOUL", "GAME_PIECE", "GREMLIN_HORN", "HAND_DRILL", "HAPPY_FLOWER",
        "HORN_CLEAT", "ICE_CREAM", "IRON_CLUB", "KUNAI", "KUSARIGAMA", "LANTERN", "LETTER_OPENER", "LIZARD_TAIL",
        "LOST_WISP", "MERCURY_HOURGLASS"
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("RELICS_1_1_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class CardDouble : CardDef
    {
        public CardDouble(string id, CardType type)
            : base(id, 1, type, CardRarity.Common, type == CardType.Attack ? TargetType.AnyEnemy : TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0, null);
        var cs = new CombatState(run, player, "RELICS_1_1_TEST");
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    private static CardInstance AddCard(CombatState cs, CardPile pile, CardDef def)
    {
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        pile.AddInternal(card);
        return card;
    }

    public static void Test_Content_relics_1_1_AllPortedIdsAreRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(RelicRegistry.Models.Contains(id), $"relic {id} is not registered");
            Assert.Equal(id, RelicRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_relics_1_1_BeatingRemnantCapsHpLossPerTurn()
    {
        var cs = NewCombat();
        var relic = Equip(cs, new BeatingRemnant());
        var monster = cs.Monsters[0];
        Assert.Equal(20m, relic.ModifyHpLostAfterOsty(cs.Run, cs.Player, 30m, ValueProp.Move, monster, null));
        Assert.Equal(30m, relic.ModifyHpLostAfterOsty(cs.Run, monster, 30m, ValueProp.Move, cs.Player, null), "other creatures are not capped");
        var hit = new DamageResult(cs.Player, ValueProp.Move) { UnblockedDamage = 15 };
        relic.AfterDamageReceived(cs.Run, cs.Player, hit, ValueProp.Move, monster, null);
        Assert.Equal(5m, relic.ModifyHpLostAfterOsty(cs.Run, cs.Player, 10m, ValueProp.Move, monster, null));
        relic.BeforeSideTurnStart(cs, CombatSide.Enemy, new CreatureState[] { monster });
        Assert.Equal(5m, relic.ModifyHpLostAfterOsty(cs.Run, cs.Player, 10m, ValueProp.Move, monster, null), "the enemy turn start does not reset");
        relic.BeforeSideTurnStart(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(10m, relic.ModifyHpLostAfterOsty(cs.Run, cs.Player, 10m, ValueProp.Move, monster, null));
    }

    public static void Test_Content_relics_1_1_KusarigamaHitsOnEveryThirdAttackAndResetsAtTurnEnd()
    {
        var cs = NewCombat();
        var relic = Equip(cs, new Kusarigama());
        var attack = AddCard(cs, cs.Player.Hand, new CardDouble("RELICS_1_1_ATTACK", CardType.Attack));
        var skill = AddCard(cs, cs.Player.Hand, new CardDouble("RELICS_1_1_SKILL", CardType.Skill));
        var monster = cs.Monsters[0];
        relic.AfterCardPlayed(cs, new CardPlay(cs, skill, null));
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        Assert.Equal(RelicStatus.Active, relic.Status);
        Assert.Equal(50, monster.CurrentHp);
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        Assert.Equal(44, monster.CurrentHp);
        Assert.Equal(RelicStatus.Normal, relic.Status);
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        relic.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        relic.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        Assert.Equal(44, monster.CurrentHp, "the counter resets at the owner's turn end");
    }

    public static void Test_Content_relics_1_1_BrilliantScarfMakesTheFifthCardFree()
    {
        var cs = NewCombat();
        var relic = Equip(cs, new BrilliantScarf());
        var def = new CardDouble("RELICS_1_1_SKILL", CardType.Skill);
        var inHand = AddCard(cs, cs.Player.Hand, def);
        var inDiscard = AddCard(cs, cs.Player.DiscardPile, def);
        for (int i = 0; i < 4; i++)
        {
            Assert.False(relic.TryModifyEnergyCostInCombatLate(cs, inHand, 1m, out _), $"card {i + 1} is not free");
            relic.AfterCardPlayed(cs, new CardPlay(cs, inHand, null));
        }
        Assert.Equal(RelicStatus.Active, relic.Status);
        Assert.True(relic.TryModifyEnergyCostInCombatLate(cs, inHand, 1m, out var cost));
        Assert.Equal(0m, cost);
        Assert.True(relic.TryModifyStarCost(cs, inHand, 2m, out var stars));
        Assert.Equal(0m, stars);
        Assert.False(relic.TryModifyEnergyCostInCombatLate(cs, inDiscard, 1m, out _), "only hand or play pile cards");
        var auto = new CardPlay(cs, inHand, null) { IsAutoPlay = true };
        relic.AfterCardPlayed(cs, auto);
        Assert.True(relic.TryModifyEnergyCostInCombatLate(cs, inHand, 1m, out _), "auto-plays do not count");
        relic.BeforeSideTurnStart(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.False(relic.TryModifyEnergyCostInCombatLate(cs, inHand, 1m, out _));
    }
}
