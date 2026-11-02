using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_3_3Tests
{
    private static readonly string[] PortedIds =
    {
        "PRECISE_CUT", "PRIMAL_FORCE", "RAMPAGE", "REAVE", "REBOUND", "REFLEX", "REGRET", "REND", "RICOCHET",
        "ROCKET_PUNCH", "SCRAPE", "SEVERANCE", "SHADOW_STEP", "SHIV", "SOUL_STORM", "STACK", "STAMPEDE",
        "STORM_OF_STEEL", "TACTICIAN", "THRASH", "TIMES_UP", "TURBO", "UNDEATH", "UNTOUCHABLE", "UPROAR",
        "UP_MY_SLEEVE", "VOID"
    };

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Content_cards_3_3_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id + " is registered");
            Assert.False(def is UnportedCardDef, id + " is a ported def");
            Assert.Equal(id, def.Id);
            def.CreateVars();
        }
    }

    public static void Test_Content_cards_3_3_RampageGrowsAndSurvivesDowngrade()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 100);
        var rampage = MakeIn(cs, PileType.Play, Rampage.CardId);
        rampage.OnPlay(cs, new CardPlay(cs, rampage, monster));
        rampage.OnPlay(cs, new CardPlay(cs, rampage, monster));
        Assert.Equal(100 - 10 - 15, monster.CurrentHp);
        Assert.Equal(20m, rampage.Vars.Damage.BaseValue);
        rampage.Upgrade();
        rampage.DowngradeInternal();
        Assert.Equal(20m, rampage.Vars.Damage.BaseValue, "AfterDowngraded re-adds ExtraDamageFromPlays");
    }

    public static void Test_Content_cards_3_3_PreciseCutSubtractsOtherHandCards()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 100);
        MakeIn(cs, PileType.Hand, StrikeIronclad.CardId);
        MakeIn(cs, PileType.Hand, DefendIronclad.CardId);
        var preciseCut = MakeIn(cs, PileType.Play, PreciseCut.CardId);
        preciseCut.OnPlay(cs, new CardPlay(cs, preciseCut, monster));
        Assert.Equal(100 - (13 - 2 * 2), monster.CurrentHp);
    }

    public static void Test_Content_cards_3_3_VoidDrainsEnergyWhenDrawn()
    {
        var cs = NewCombat();
        AddMonster(cs);
        MakeIn(cs, PileType.Draw, Core.Cards.Void.CardId);
        CardPileCmd.Draw(cs, 1m, cs.Player);
        Assert.Equal(2, cs.Player.Energy);
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CONTENT_CARDS_3_3_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_3_3_IDLE", static (_, _, _) => { });
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
            : base("CONTENT_CARDS_3_3_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
