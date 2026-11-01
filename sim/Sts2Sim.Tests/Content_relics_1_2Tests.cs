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

public static class Content_relics_1_2Tests
{
    private static readonly string[] PortedIds =
    {
        "MINIATURE_CANNON", "MR_STRUGGLES", "NUNCHAKU", "ORICHALCUM", "ORNAMENTAL_FAN", "PAELS_BLOOD", "PAELS_FLESH",
        "PARRYING_SHIELD", "PENDULUM", "PEN_NIB", "POCKETWATCH", "POLLINOUS_CORE", "RAINBOW_RING", "RED_MASK", "REGALITE",
        "RINGING_TRIANGLE", "RING_OF_THE_DRAKE", "RING_OF_THE_SNAKE", "ROYAL_POISON", "RUNIC_PYRAMID", "SAI",
        "SCREAMING_FLAGON", "SHURIKEN", "SPARKLING_ROUGE", "SPIKED_GAUNTLETS", "STONE_CALENDAR", "STRIKE_DUMMY",
        "STURDY_CLAMP", "THE_ABACUS", "THE_BOOT", "TINGSHA", "TOUGH_BANDAGES", "TUNGSTEN_ROD", "TUNING_FORK",
        "UNCEASING_TOP", "VAMBRACE", "VELVET_CHOKER", "VERY_HOT_COCOA", "VITRUVIAN_MINION",
        "WONGO_CUSTOMER_APPRECIATION_BADGE"
    };

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("RELICS_1_2_TEST_MONSTER")
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
        var run = RunState.CreateStandalone(player, new RunRngSet(777UL), 0, null);
        var cs = new CombatState(run, player, "RELICS_1_2_TEST");
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

    public static void Test_ContentRelics12_EveryPortedIdIsRegistered()
    {
        var all = new HashSet<string>(RelicRegistry.AllIds);
        foreach (var id in PortedIds)
        {
            Assert.True(all.Contains(id), id);
            Assert.Equal(id, RelicRegistry.Create(id).Id);
        }
    }

    public static void Test_ContentRelics12_PenNibDoublesTheTenthAttackOnly()
    {
        var cs = NewCombat();
        var nib = Equip(cs, new PenNib());
        var attack = new CardDouble("RELICS_1_2_ATTACK", CardType.Attack);
        var monster = cs.Monsters[0];
        for (int i = 0; i < 9; i++)
        {
            var c = AddCard(cs, cs.Player.PlayPile, attack);
            var play = new CardPlay(cs, c, monster);
            nib.BeforeCardPlayed(cs, play);
            Assert.Equal(1m, nib.ModifyDamageMultiplicative(cs.Run, monster, 6m, ValueProp.Move, cs.Player, c, play), "attack " + i);
            nib.AfterCardPlayed(cs, play);
        }
        Assert.Equal(9, nib.AttacksPlayed);
        var hand = AddCard(cs, cs.Player.Hand, attack);
        Assert.Equal(2m, nib.ModifyDamageMultiplicative(cs.Run, monster, 6m, ValueProp.Move, cs.Player, hand, null), "preview of the tenth attack in hand");
        var tenth = AddCard(cs, cs.Player.PlayPile, attack);
        var tenthPlay = new CardPlay(cs, tenth, monster);
        nib.BeforeCardPlayed(cs, tenthPlay);
        Assert.Equal(0, nib.AttacksPlayed);
        Assert.Equal(tenth.InstanceId, nib.AttackToDoubleId);
        Assert.Equal(2m, nib.ModifyDamageMultiplicative(cs.Run, monster, 6m, ValueProp.Move, cs.Player, tenth, tenthPlay));
        Assert.Equal(1m, nib.ModifyDamageMultiplicative(cs.Run, monster, 6m, ValueProp.Move, cs.Player, hand, null), "only the chosen card");
        nib.AfterCardPlayed(cs, tenthPlay);
        Assert.Equal(PenNib.NoCard, nib.AttackToDoubleId);
    }

    public static void Test_ContentRelics12_OrichalcumAndSturdyClamp()
    {
        var cs = NewCombat();
        var ori = Equip(cs, new Orichalcum());
        var participants = new CreatureState[] { cs.Player };
        ori.BeforeSideTurnEndVeryEarly(cs, CombatSide.Player, participants);
        ori.BeforeSideTurnEnd(cs, CombatSide.Player, participants);
        Assert.Equal(6, cs.Player.Block);
        ori.BeforeSideTurnEndVeryEarly(cs, CombatSide.Player, participants);
        ori.BeforeSideTurnEnd(cs, CombatSide.Player, participants);
        Assert.Equal(6, cs.Player.Block, "no trigger with block");

        var cs2 = NewCombat();
        var clamp = Equip(cs2, new SturdyClamp());
        CreatureCmd.GainBlock(cs2, cs2.Player, 15m, ValueProp.Unpowered, null);
        CreatureCmd.ClearBlock(cs2, cs2.Player);
        Assert.Equal(10, cs2.Player.Block);
        Assert.False(clamp.ShouldClearBlock(cs2, cs2.Player));
    }
}
