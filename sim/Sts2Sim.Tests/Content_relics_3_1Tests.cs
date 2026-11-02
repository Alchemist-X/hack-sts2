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

public static class Content_relics_3_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ART_OF_WAR", "BELLOWS", "BLACK_BLOOD", "BLESSED_ANTLER", "BONE_TEA", "BOOKMARK", "BOOK_REPAIR_KNIFE",
        "BOOMING_CONCH", "BRONZE_SCALES", "BURNING_BLOOD", "BURNING_STICKS", "CENTENNIAL_PUZZLE", "CHOSEN_CHEESE",
        "DATA_DISK", "EMBER_TEA", "GHOST_SEED", "GORGET", "INTIMIDATING_HELMET", "IVORY_TILE", "JEWELED_MASK",
        "JOSS_PAPER", "MEAT_ON_THE_BONE", "MUMMIFIED_HAND",
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("RELICS_3_1_TEST_MONSTER")
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
        var run = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0);
        var cs = new CombatState(run, player, "RELICS_3_1_TEST");
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, new MonsterDouble(), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static CardInstance AddCard(CombatState cs, CardPile pile, CardDef def)
    {
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        pile.AddInternal(card);
        return card;
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_3_1_AllPortedIdsAreRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id);
        }
    }

    public static void Test_Content_relics_3_1_JossPaperCountsExhaustsAndDefersEthereal()
    {
        var cs = NewCombat();
        var joss = Equip(cs, new JossPaper());
        var def = new CardDouble("RELICS_3_1_SKILL", CardType.Skill);
        for (int i = 0; i < 4; i++)
        {
            AddCard(cs, cs.Player.DrawPile, def);
        }
        var exhausted = AddCard(cs, cs.Player.ExhaustPile, def);
        for (int i = 0; i < 4; i++)
        {
            joss.AfterCardExhausted(cs, exhausted, false);
        }
        Assert.Equal(4, joss.CardsExhausted);
        Assert.Equal(RelicStatus.Active, joss.Status);
        Assert.Equal(0, cs.Player.Hand.Cards.Count);
        joss.AfterCardExhausted(cs, exhausted, true);
        Assert.Equal(4, joss.CardsExhausted, "an ethereal exhaust waits for the side turn end");
        joss.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(0, joss.CardsExhausted);
        Assert.Equal(1, cs.Player.Hand.Cards.Count);
        Assert.Equal(RelicStatus.Normal, joss.Status);
    }

    public static void Test_Content_relics_3_1_ArtOfWarGrantsEnergyOnlyAfterAnAttacklessTurn()
    {
        var cs = NewCombat();
        var art = Equip(cs, new ArtOfWar());
        var attack = AddCard(cs, cs.Player.Hand, new CardDouble("RELICS_3_1_ATTACK", CardType.Attack));
        var participants = new CreatureState[] { cs.Player };
        art.AfterEnergyReset(cs, cs.Player);
        art.AfterCardPlayed(cs, new CardPlay(cs, attack, null));
        art.AfterSideTurnEnd(cs, CombatSide.Player, participants);
        cs.Player.TurnNumber = 2;
        int energy = cs.Player.Energy;
        art.AfterEnergyReset(cs, cs.Player);
        Assert.Equal(energy, cs.Player.Energy, "an attack last turn blocks the energy");
        art.AfterSideTurnEnd(cs, CombatSide.Player, participants);
        cs.Player.TurnNumber = 3;
        art.AfterEnergyReset(cs, cs.Player);
        Assert.Equal(energy + 1, cs.Player.Energy);
    }
}
