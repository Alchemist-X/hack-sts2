using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_1_2Tests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    private static readonly string[] PortedIds =
    {
        "DEFILE", "DEFLECT", "DEFY", "DISMANTLE", "DOMINATE", "DOUBLE_ENERGY", "DOUBT", "DRAMATIC_ENTRANCE",
        "ENLIGHTENMENT", "ENTRENCH", "ERADICATE", "ESCAPE_PLAN", "EXTERMINATE", "FEAR", "FEED", "FIEND_FIRE",
        "FIGHT_ME", "FINESSE", "FISTICUFFS", "FLASH_OF_STEEL", "FOLLY", "FOOTWORK", "FORGOTTEN_RITUAL", "FUEL",
        "GIANT_ROCK", "GO_FOR_THE_EYES", "GRAND_FINALE", "GUNK_UP", "HEAVENLY_DRILL", "HEMOKINESIS", "IMPATIENCE",
        "IMPERVIOUS", "INFECTION", "INFLAME", "INJURY"
    };

    public static void Test_Content_cards_1_2_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.Equal(id, def.Id);
            Assert.False(def is UnportedCardDef, id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name));
        }
    }

    public static void Test_Content_cards_1_2_DominateGrantsStrengthPerVulnerable()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs);
        var dominate = cs.CreateCard(Dominate.CardId, cs.Player);
        dominate.Upgrade();
        dominate.OnPlay(cs, new CardPlay(cs, dominate, monster));
        Assert.Equal(2, Assert.NotNull(monster.GetPower<VulnerablePower>()).Amount);
        Assert.Equal(2, Assert.NotNull(cs.Player.GetPower<StrengthPower>()).Amount);
    }

    public static void Test_Content_cards_1_2_FisticuffsBlocksDamageIncludingOverkill()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 4);
        AddMonster(cs);
        var fisticuffs = cs.CreateCard(Fisticuffs.CardId, cs.Player);
        fisticuffs.OnPlay(cs, new CardPlay(cs, fisticuffs, monster));
        Assert.Equal(7, cs.Player.Block);
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = 3;
        var cs = new CombatState(player, "CONTENT_CARDS_1_2", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_1_2_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_1_2_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
