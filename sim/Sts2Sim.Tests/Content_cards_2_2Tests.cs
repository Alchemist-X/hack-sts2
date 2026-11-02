using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_2_2Tests
{
    private static readonly string[] PortedIds =
    {
        "EQUILIBRIUM", "ETERNAL_ARMOR", "EXPOSE", "FASTEN", "FEEDING_FRENZY", "FEEL_NO_PAIN", "FIGHT_THROUGH",
        "FLAME_BARRIER", "FRANTIC_ESCAPE", "FRIENDSHIP", "GLITTERSTREAM", "HANG", "HAZE", "HEGEMONY", "INFERNO",
        "JUGGERNAUT", "MACHINE_LEARNING", "MANGLE", "MONARCHS_GAZE", "NEGATIVE_PULSE", "NEUROSURGE", "NOXIOUS_FUMES",
        "OBLIVION", "ONE_TWO_PUNCH", "ORBIT", "OUTBREAK", "OUTMANEUVER", "OVERCLOCK", "PAGESTORM", "PANACHE",
        "PANIC_BUTTON", "PATTER", "PIERCING_WAIL", "PILLAR_OF_CREATION", "POISONED_STAB", "POUNCE"
    };

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Content_cards_2_2_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id + " is registered");
            Assert.False(def is UnportedCardDef, id + " is a ported def");
            Assert.Equal(id, def.Id);
            Assert.Equal(id, DeterministicHash.Slugify(def.GetType().Name));
        }
    }

    public static void Test_Content_cards_2_2_ExposeStripsBlockThenAppliesVulnerable()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs);
        CreatureCmd.GainBlock(cs, monster, 7m, ValueProp.None, null);
        Assert.Equal(7, monster.Block);
        var card = MakeIn(cs, PileType.Play, Expose.CardId);
        card.OnPlay(cs, new CardPlay(cs, card, monster));
        Assert.Equal(0, monster.Block);
        Assert.Equal((int)Expose.BasePower, monster.GetPowerAmount<VulnerablePower>());
    }

    public static void Test_Content_cards_2_2_FranticEscapeCostGrowsEachPlay()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var card = MakeIn(cs, PileType.Play, FranticEscape.CardId);
        Assert.Equal(FranticEscape.Cost, card.EnergyCost.GetResolved(cs, card));
        card.OnPlay(cs, new CardPlay(cs, card, null));
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(FranticEscape.Cost + 2 * FranticEscape.CostIncreasePerPlay, card.EnergyCost.GetResolved(cs, card));
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CONTENT_CARDS_2_2_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_2_2_IDLE", static (_, _, _) => { });
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
            : base("CONTENT_CARDS_2_2_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
