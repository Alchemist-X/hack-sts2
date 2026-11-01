using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_1_3Tests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    private static readonly (string Id, Type Type)[] Ported =
    {
        (IronWave.CardId, typeof(IronWave)),
        (KnowThyPlace.CardId, typeof(KnowThyPlace)),
        (Leap.CardId, typeof(Leap)),
        (LegSweep.CardId, typeof(LegSweep)),
        (Luminesce.CardId, typeof(Luminesce)),
        (Malaise.CardId, typeof(Malaise)),
        (MasterOfStrategy.CardId, typeof(MasterOfStrategy)),
        (MinionDiveBomb.CardId, typeof(MinionDiveBomb)),
        (MinionSacrifice.CardId, typeof(MinionSacrifice)),
        (MinionStrike.CardId, typeof(MinionStrike)),
        (MoltenFist.CardId, typeof(MoltenFist)),
        (MomentumStrike.CardId, typeof(MomentumStrike)),
        (Neutralize.CardId, typeof(Neutralize)),
        (NotYet.CardId, typeof(NotYet)),
        (Offering.CardId, typeof(Offering)),
        (PactsEnd.CardId, typeof(PactsEnd)),
        (Parse.CardId, typeof(Parse)),
        (Peck.CardId, typeof(Peck)),
        (Pillage.CardId, typeof(Pillage)),
        (PommelStrike.CardId, typeof(PommelStrike)),
        (PoorSleep.CardId, typeof(PoorSleep)),
        (Production.CardId, typeof(Production)),
        (Prophesize.CardId, typeof(Prophesize)),
        (Prowess.CardId, typeof(Prowess)),
        (Putrefy.CardId, typeof(Putrefy)),
        (Reap.CardId, typeof(Reap)),
        (Reboot.CardId, typeof(Reboot)),
        (Restlessness.CardId, typeof(Restlessness)),
        (RipAndTear.CardId, typeof(RipAndTear)),
        (Scrawl.CardId, typeof(Scrawl)),
        (SecondWind.CardId, typeof(SecondWind)),
        (Shame.CardId, typeof(Shame)),
        (SharedFate.CardId, typeof(SharedFate)),
        (Shockwave.CardId, typeof(Shockwave)),
        (ShrugItOff.CardId, typeof(ShrugItOff)),
    };

    public static void Test_Content_cards_1_3_AllPortedIdsRegistered()
    {
        foreach (var (id, type) in Ported)
        {
            Assert.Equal(DeterministicHash.Slugify(type.Name), id);
            Assert.True(CardRegistry.Defs.Contains(id), id);
            Assert.True(CardRegistry.Get(id).GetType() == type, id);
            var entry = CardCatalog.Get(id);
            var def = CardRegistry.Get(id);
            Assert.Equal(entry.CanonicalCost, def.CanonicalCost, id);
            Assert.Equal(entry.CostsX, def.CostsX, id);
            Assert.Equal(entry.Type, def.Type, id);
            Assert.Equal(entry.Rarity, def.Rarity, id);
            Assert.Equal(entry.TargetType, def.TargetType, id);
            Assert.Equal(entry.CanBeGeneratedInCombat, def.CanBeGeneratedInCombat, id);
            Assert.Equal(entry.CanBeGeneratedByModifiers, def.CanBeGeneratedByModifiers, id);
        }
    }

    public static void Test_Content_cards_1_3_SecondWindExhaustsNonAttacksAndBlocksPerCard()
    {
        var cs = NewCombat();
        AddMonster(cs, 40);
        var hand = cs.Player.Hand;
        MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        MakeIn(cs, PileType.Hand, Shame.CardId);
        var card = MakeIn(cs, PileType.Play, SecondWind.CardId);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(1, hand.Cards.Count);
        Assert.Equal(StrikeIronclad.CardId, hand.Cards[0].Id);
        Assert.Equal(2, cs.Player.ExhaustPile.Cards.Count);
        Assert.Equal(10, cs.Player.Block);
    }

    public static void Test_Content_cards_1_3_PactsEndNeedsThreeExhaustedCards()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 60);
        var card = MakeIn(cs, PileType.Play, PactsEnd.CardId);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(60, monster.CurrentHp);
        for (int i = 0; i < 3; i++)
        {
            MakeIn(cs, PileType.Exhaust, Shame.CardId);
        }
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(42, monster.CurrentHp);
    }

    public static void Test_Content_cards_1_3_ShameAppliesFrailThatSkipsFirstTick()
    {
        var cs = NewCombat();
        AddMonster(cs, 40);
        var card = MakeIn(cs, PileType.Hand, Shame.CardId);
        card.OnTurnEndInHand(cs);
        var frail = Assert.NotNull(cs.Player.GetPower<FrailPower>());
        Assert.Equal(1, frail.Amount);
        Assert.True(frail.SkipNextDurationTick);
    }

    private static CardInstance MakeIn(CombatState cs, PileType pile, string id)
    {
        var card = cs.CreateCard(CardRegistry.Get(id), cs.Player);
        cs.Player.GetPile(pile).AddInternal(card);
        return card;
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = 3;
        var cs = new CombatState(player, "CONTENT_CARDS_1_3", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_1_3_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_1_3_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
