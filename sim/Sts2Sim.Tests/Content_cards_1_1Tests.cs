using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_1_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ADRENALINE", "ALL_FOR_ONE", "ANOINTED", "ASSASSINATE", "BACKFLIP", "BACKSTAB", "BAD_LUCK", "BEAM_CELL",
        "BECKON", "BLOODLETTING", "BLOOD_WALL", "BLUDGEON", "BOOT_SEQUENCE", "BREAK", "BREAKTHROUGH", "BRIGHTEST_FLAME",
        "BURN", "BURY", "BYRD_SWOOP", "CALTROPS", "CELESTIAL_MIGHT", "CINDER", "CLASH", "CLUMSY", "CONFLAGRATION",
        "CURSE_OF_THE_BELL", "DAGGER_SPRAY", "DASH", "DAZED", "DEBRIS", "DECAY", "DEFEND_DEFECT", "DEFEND_NECROBINDER",
        "DEFEND_REGENT", "DEFEND_SILENT"
    };

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Content_cards_1_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id + " is registered");
            Assert.False(def is UnportedCardDef, id + " is a ported def");
            Assert.Equal(id, def.Id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name));
        }
    }

    public static void Test_Content_cards_1_1_AllForOneReturnsZeroCostCardsFromDiscard()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs);
        var zero = MakeIn(cs, PileType.Discard, Backstab.CardId);
        var paid = MakeIn(cs, PileType.Discard, Backflip.CardId);
        var status = MakeIn(cs, PileType.Discard, Dazed.CardId);
        var card = MakeIn(cs, PileType.Play, AllForOne.CardId);
        card.OnPlay(cs, new CardPlay(cs, card, monster));
        Assert.Equal(40 - (int)AllForOne.BaseDamage, monster.CurrentHp);
        Assert.True(cs.Player.Hand.Contains(zero), "0-cost attack returns to hand");
        Assert.True(cs.Player.DiscardPile.Contains(paid), "1-cost skill stays in discard");
        Assert.True(cs.Player.DiscardPile.Contains(status), "unplayable status stays in discard");
    }

    public static void Test_Content_cards_1_1_ClashOnlyPlayableWithAllAttacksInHand()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var clash = MakeIn(cs, PileType.Hand, Clash.CardId);
        MakeIn(cs, PileType.Hand, Bludgeon.CardId);
        Assert.True(clash.Def.IsPlayable(cs, clash), "attacks only");
        MakeIn(cs, PileType.Hand, Backflip.CardId);
        Assert.False(clash.Def.IsPlayable(cs, clash), "a skill in hand blocks Clash");
    }

    public static void Test_Content_cards_1_1_BloodlettingLosesHpThenGainsEnergy()
    {
        var cs = NewCombat(energy: 0);
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Play, Bloodletting.CardId);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(80 - (int)Bloodletting.BaseHpLoss, cs.Player.CurrentHp);
        Assert.Equal(Bloodletting.BaseEnergy, cs.Player.Energy);
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CONTENT_CARDS_1_1_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_1_1_IDLE", static (_, _, _) => { });
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

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_1_1_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
