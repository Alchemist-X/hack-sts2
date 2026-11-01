using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_9_1Tests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    private static readonly (string Id, Type Type)[] Ported =
    {
        ("AFTERLIFE", typeof(Afterlife)),
        ("ALIGNMENT", typeof(Alignment)),
        ("ASTRAL_PULSE", typeof(AstralPulse)),
        ("BEAT_INTO_SHAPE", typeof(BeatIntoShape)),
        ("BIASED_COGNITION", typeof(BiasedCognition)),
        ("BIG_BANG", typeof(BigBang)),
        ("BLACK_HOLE", typeof(BlackHole)),
        ("BODYGUARD", typeof(Bodyguard)),
        ("BONE_SHARDS", typeof(BoneShards)),
        ("BULWARK", typeof(Bulwark)),
        ("CALCIFY", typeof(Calcify)),
        ("CHILD_OF_THE_STARS", typeof(ChildOfTheStars)),
        ("CLEANSE", typeof(Cleanse)),
        ("CLOAK_OF_STARS", typeof(CloakOfStars)),
        ("COMET", typeof(Comet)),
        ("CONQUEROR", typeof(Conqueror)),
        ("CONVERGENCE", typeof(Convergence)),
        ("CRESCENT_SPEAR", typeof(CrescentSpear)),
        ("DECISIONS_DECISIONS", typeof(DecisionsDecisions)),
        ("DEVASTATE", typeof(Devastate)),
        ("DEVOUR_LIFE", typeof(DevourLife)),
        ("DIRGE", typeof(Dirge)),
        ("DYING_STAR", typeof(DyingStar)),
        ("FALLING_STAR", typeof(FallingStar)),
        ("FETCH", typeof(Fetch)),
        ("FLATTEN", typeof(Flatten)),
        ("FURNACE", typeof(Furnace)),
        ("GAMMA_BLAST", typeof(GammaBlast)),
        ("GATHER_LIGHT", typeof(GatherLight)),
        ("GENESIS", typeof(Genesis)),
        ("GLOW", typeof(Glow)),
        ("GUIDING_STAR", typeof(GuidingStar)),
        ("HIDDEN_CACHE", typeof(HiddenCache)),
        ("HIGH_FIVE", typeof(HighFive)),
        ("INVOKE", typeof(Invoke)),
        ("KNOCKOUT_BLOW", typeof(KnockoutBlow)),
        ("METEOR_SHOWER", typeof(MeteorShower)),
        ("NECRO_MASTERY", typeof(NecroMastery)),
    };

    public static void Test_Content_cards_9_1_AllPortedIdsRegistered()
    {
        foreach (var (id, type) in Ported)
        {
            Assert.Equal(id, DeterministicHash.Slugify(type.Name), type.Name);
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.Equal(type, def.GetType(), id);
            Assert.Equal(id, def.Id, id);
        }
    }

    public static void Test_Content_cards_9_1_KnockoutBlowGainsStarsOnlyOnKill()
    {
        var cs = NewCombat();
        var weak = AddMonster(cs, 20);
        var tough = AddMonster(cs, 80);
        var card = cs.CreateCard(KnockoutBlow.CardId, cs.Player);
        cs.Player.Hand.AddInternal(card);
        card.OnPlay(cs, new CardPlay(cs, card, tough));
        Assert.Equal(50, tough.CurrentHp);
        Assert.Equal(0, cs.Player.Stars, "no kill, no stars");
        card.OnPlay(cs, new CardPlay(cs, card, weak));
        Assert.True(weak.IsDead);
        Assert.Equal(KnockoutBlow.BaseStars, cs.Player.Stars, "kill grants stars");
    }

    public static void Test_Content_cards_9_1_CrescentSpearCountsStarCostCards()
    {
        var cs = NewCombat();
        var target = AddMonster(cs, 100);
        var card = cs.CreateCard(CrescentSpear.CardId, cs.Player);
        cs.Player.Hand.AddInternal(card);
        cs.Player.DrawPile.AddInternal(cs.CreateCard(Comet.CardId, cs.Player));
        cs.Player.DiscardPile.AddInternal(cs.CreateCard(GatherLight.CardId, cs.Player));
        cs.Player.DiscardPile.AddInternal(cs.CreateCard(FallingStar.CardId, cs.Player));
        card.OnPlay(cs, new CardPlay(cs, card, target));
        Assert.Equal(100 - (8 + 2 * 3), target.CurrentHp, "spear + comet + falling star have star costs; gather light does not");
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "REGENT", 75, 75, 3);
        player.Energy = 3;
        var cs = new CombatState(player, "CONTENT_CARDS_9_1", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_9_1_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_9_1_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
