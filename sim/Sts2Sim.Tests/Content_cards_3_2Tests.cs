using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_3_2Tests
{
    private static readonly string[] PortedIds =
    {
        "EXPECT_A_FIGHT", "EXPERTISE", "FAN_OF_KNIVES", "FLAK_CANNON", "FLECHETTES", "FLICK_FLACK", "GRAVE_WARDEN",
        "HAUNT", "HAVOC", "HELLRAISER", "HIDDEN_GEM", "HOWL_FROM_BEYOND", "INFINITE_BLADES", "I_AM_INVINCIBLE",
        "KINGLY_KICK", "KINGLY_PUNCH", "KNIFE_TRAP", "LEADING_STRIKE", "MASTER_PLANNER", "MAUL", "MAYHEM",
        "MELANCHOLY", "MIND_BLAST", "MIRAGE", "MONOLOGUE", "NO_ESCAPE", "OMNISLICE", "PERFECTED_STRIKE"
    };

    private static readonly DummyMonster DummyMonsterDef = new();

    public static void Test_Content_cards_3_2_AllPortedIdsAreRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(CardRegistry.TryGet(id, out var def), id + " is registered");
            Assert.False(def is UnportedCardDef, id + " has a ported def");
            Assert.Equal(id, def.Id, "registry key matches the def id");
        }
    }

    public static void Test_Content_cards_3_2_MaulBuffsEveryMaulInCombatAndSurvivesDowngrade()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 60);
        var maul = MakeIn(cs, PileType.Hand, Maul.CardId);
        var other = MakeIn(cs, PileType.Draw, Maul.CardId);
        CardCmd.AutoPlay(cs, maul, monster);
        Assert.Equal(50, monster.CurrentHp, "two hits of 5");
        Assert.Equal(Maul.BaseDamage + Maul.BaseIncrease, other.Vars.Damage.BaseValue, "the other Maul gained Increase");
        Assert.Equal(Maul.BaseDamage + Maul.BaseIncrease, maul.Vars.Damage.BaseValue, "the played Maul buffs itself too");
        other.Upgrade();
        CardCmd.Downgrade(cs, other);
        Assert.Equal(Maul.BaseDamage + Maul.BaseIncrease, other.Vars.Damage.BaseValue, "AfterDowngraded re-adds the Maul-play bonus");
    }

    public static void Test_Content_cards_3_2_OmnisliceSplashesTotalPlusOverkillUnpowered()
    {
        var cs = NewCombat();
        var target = AddMonster(cs, 5);
        var other = AddMonster(cs, 40);
        var omnislice = MakeIn(cs, PileType.Hand, Omnislice.CardId);
        CardCmd.AutoPlay(cs, omnislice, target);
        Assert.True(target.IsDead, "the target dies");
        Assert.Equal(40 - (int)Omnislice.BaseDamage, other.CurrentHp, "the splash is TotalDamage + Overkill");
    }

    public static void Test_Content_cards_3_2_KinglyPunchGrowsWhenDrawn()
    {
        var cs = NewCombat();
        AddMonster(cs);
        var punch = MakeIn(cs, PileType.Draw, KinglyPunch.CardId);
        CardPileCmd.Draw(cs, cs.Player);
        Assert.Equal(KinglyPunch.BaseDamage + KinglyPunch.BaseIncrease, punch.Vars.Damage.BaseValue, "drawing adds Increase");
        Assert.Equal(KinglyPunch.BaseIncrease, KinglyPunch.GetExtraDamage(punch), "the extra damage is tracked");
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CONTENT_CARDS_3_2_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp = 40)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_3_2_IDLE", static (_, _, _) => { });
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
            : base("CONTENT_CARDS_3_2_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
