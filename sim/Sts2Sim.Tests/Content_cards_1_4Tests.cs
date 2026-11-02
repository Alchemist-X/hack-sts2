using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_1_4Tests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    private static readonly (string Id, Type Type)[] Ported =
    {
        (Skewer.CardId, typeof(Skewer)),
        (Skim.CardId, typeof(Skim)),
        (Slice.CardId, typeof(Slice)),
        (Soot.CardId, typeof(Soot)),
        (Sow.CardId, typeof(Sow)),
        (SporeMind.CardId, typeof(SporeMind)),
        (Squash.CardId, typeof(Squash)),
        (StrikeDefect.CardId, typeof(StrikeDefect)),
        (StrikeNecrobinder.CardId, typeof(StrikeNecrobinder)),
        (StrikeRegent.CardId, typeof(StrikeRegent)),
        (StrikeSilent.CardId, typeof(StrikeSilent)),
        (SuckerPunch.CardId, typeof(SuckerPunch)),
        (Sunder.CardId, typeof(Sunder)),
        (Supercritical.CardId, typeof(Supercritical)),
        (Suppress.CardId, typeof(Suppress)),
        (SweepingBeam.CardId, typeof(SweepingBeam)),
        (SwordBoomerang.CardId, typeof(SwordBoomerang)),
        (Taunt.CardId, typeof(Taunt)),
        (Thunderclap.CardId, typeof(Thunderclap)),
        (Toxic.CardId, typeof(Toxic)),
        (Tremble.CardId, typeof(Tremble)),
        (TwinStrike.CardId, typeof(TwinStrike)),
        (UltimateDefend.CardId, typeof(UltimateDefend)),
        (UltimateStrike.CardId, typeof(UltimateStrike)),
        (Uppercut.CardId, typeof(Uppercut)),
        (Volley.CardId, typeof(Volley)),
        (Whirlwind.CardId, typeof(Whirlwind)),
        (Whistle.CardId, typeof(Whistle)),
        (Wisp.CardId, typeof(Wisp)),
        (Wither.CardId, typeof(Wither)),
        (Wound.CardId, typeof(Wound)),
        (Writhe.CardId, typeof(Writhe)),
    };

    public static void Test_Content_cards_1_4_AllPortedIdsRegistered()
    {
        foreach (var (id, type) in Ported)
        {
            Assert.Equal(id, DeterministicHash.Slugify(type.Name), $"{type.Name} id");
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.Equal(type, def.GetType(), id);
            def.CreateVars();
        }
    }

    public static void Test_Content_cards_1_4_SunderRefundsEnergyOnlyOnKill()
    {
        var cs = NewCombat(energy: 0);
        var big = AddMonster(cs, 100);
        var sunder = MakeIn(cs, PileType.Play, Sunder.CardId);
        sunder.OnPlay(cs, new CardPlay(cs, sunder, big));
        Assert.Equal(100 - (int)Sunder.BaseDamage, big.CurrentHp);
        Assert.Equal(0, cs.Player.Energy, "no kill, no energy");

        var small = AddMonster(cs, 10);
        var again = MakeIn(cs, PileType.Play, Sunder.CardId);
        again.OnPlay(cs, new CardPlay(cs, again, small));
        Assert.True(small.IsDead);
        Assert.Equal(Sunder.BaseEnergy, cs.Player.Energy, "a kill refunds the energy var");
    }

    public static void Test_Content_cards_1_4_UppercutAppliesWeakThenVulnerableAndWitherFakeUpgrades()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 100);
        var uppercut = MakeIn(cs, PileType.Play, Uppercut.CardId);
        uppercut.OnPlay(cs, new CardPlay(cs, uppercut, monster));
        Assert.Equal(100 - (int)Uppercut.BaseDamage, monster.CurrentHp);
        Assert.Equal(1, monster.GetPower<WeakPower>()?.Amount ?? 0);
        Assert.Equal(1, monster.GetPower<VulnerablePower>()?.Amount ?? 0);

        var wither = MakeIn(cs, PileType.Hand, Wither.CardId);
        var witherDef = (Wither)wither.Def;
        witherDef.FakeUpgrade(wither);
        Assert.Equal(1, witherDef.GetFakeUpgradeLevel(wither));
        Assert.Equal(Wither.BaseDamage + Wither.FakeUpgradeDamage, wither.Vars.Damage.BaseValue);
        int hp = cs.Player.CurrentHp;
        wither.OnTurnEndInHand(cs);
        Assert.Equal(hp - (int)(Wither.BaseDamage + Wither.FakeUpgradeDamage), cs.Player.CurrentHp);
    }

    private static CombatState NewCombat(int energy = 3, ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3)
        {
            Energy = energy
        };
        var cs = new CombatState(player, "CONTENT_CARDS_1_4_TESTS", 0, new RunRngSet(seed));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_1_4_IDLE", static (_, _, _) => { });
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
            : base("CONTENT_CARDS_1_4_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
