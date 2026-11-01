using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_cards_3_1Tests
{
    private static readonly DummyMonster DummyMonsterDef = new();

    private static readonly (string Id, string ClassName)[] Ported =
    {
        ("ABRASIVE", nameof(Abrasive)),
        ("ACCURACY", nameof(Accuracy)),
        ("ADAPTIVE_STRIKE", nameof(AdaptiveStrike)),
        ("ANGER", nameof(Anger)),
        ("APOTHEOSIS", nameof(Apotheosis)),
        ("ASHEN_STRIKE", nameof(AshenStrike)),
        ("BEAT_DOWN", nameof(BeatDown)),
        ("BLADE_DANCE", nameof(BladeDance)),
        ("BODY_SLAM", nameof(BodySlam)),
        ("BOMBARDMENT", nameof(Bombardment)),
        ("BULLY", nameof(Bully)),
        ("CALCULATED_GAMBLE", nameof(CalculatedGamble)),
        ("CAPTURE_SPIRIT", nameof(CaptureSpirit)),
        ("CASCADE", nameof(Cascade)),
        ("CATASTROPHE", nameof(Catastrophe)),
        ("CLAW", nameof(Claw)),
        ("CLOAK_AND_DAGGER", nameof(CloakAndDagger)),
        ("COLLISION_COURSE", nameof(CollisionCourse)),
        ("COMPACT", nameof(Compact)),
        ("CORRUPTION", nameof(Corruption)),
        ("CRASH_LANDING", nameof(CrashLanding)),
        ("DRAIN_POWER", nameof(DrainPower)),
        ("DRUM_OF_BATTLE", nameof(DrumOfBattle)),
        ("ECHOING_SLASH", nameof(EchoingSlash)),
        ("EIDOLON", nameof(Eidolon)),
        ("END_OF_DAYS", nameof(EndOfDays)),
        ("ENTHRALLED", nameof(Enthralled))
    };

    public static void Test_Content_cards_3_1_AllPortedIdsRegistered()
    {
        foreach (var (id, className) in Ported)
        {
            Assert.Equal(id, DeterministicHash.Slugify(className), className);
            Assert.True(CardRegistry.TryGet(id, out var def), id);
            Assert.Equal(id, def.Id, id);
            Assert.Equal(className, def.GetType().Name, id);
        }
    }

    public static void Test_Content_cards_3_1_ClawBuffsEveryClawInCombat()
    {
        var cs = NewCombat();
        var monster = AddMonster(cs, 40);
        var player = cs.Player;
        var played = cs.CreateCard(Claw.CardId, player);
        var inHand = cs.CreateCard(Claw.CardId, player);
        var inDiscard = cs.CreateCard(Claw.CardId, player);
        player.PlayPile.AddInternal(played);
        player.Hand.AddInternal(inHand);
        player.DiscardPile.AddInternal(inDiscard);
        played.OnPlay(cs, new CardPlay(cs, played, monster));
        Assert.Equal(37, monster.CurrentHp);
        foreach (var claw in new[] { played, inHand, inDiscard })
        {
            Assert.Equal(5m, claw.Vars.Damage.BaseValue, claw.ToString());
            Assert.Equal(2m, ((Claw)claw.Def).GetExtraDamageFromClawPlays(claw), claw.ToString());
        }
        var clone = inHand.DeepClone();
        Assert.Equal(2m, ((Claw)clone.Def).GetExtraDamageFromClawPlays(clone), "clone keeps the per-instance bonus");
        inHand.Upgrade();
        Assert.Equal(6m, inHand.Vars.Damage.BaseValue);
        CardCmd.Downgrade(cs, inHand);
        Assert.Equal(5m, inHand.Vars.Damage.BaseValue, "AfterDowngraded re-applies the bonus");
    }

    public static void Test_Content_cards_3_1_EchoingSlashRepeatsPerKill()
    {
        var cs = NewCombat();
        var weak = AddMonster(cs, 5);
        var sturdy = AddMonster(cs, 40);
        var player = cs.Player;
        var slash = cs.CreateCard(EchoingSlash.CardId, player);
        player.PlayPile.AddInternal(slash);
        slash.OnPlay(cs, new CardPlay(cs, slash, null));
        Assert.True(weak.IsDead, "the first hit kills the weak enemy");
        Assert.Equal(20, sturdy.CurrentHp, "one kill grants exactly one extra hit");
    }

    public static void Test_Content_cards_3_1_EnthralledBlocksManualPlayOnly()
    {
        var cs = NewCombat();
        AddMonster(cs, 40);
        var player = cs.Player;
        var enthralled = cs.CreateCard(Enthralled.CardId, player);
        var anger = cs.CreateCard(Anger.CardId, player);
        player.Hand.AddInternal(enthralled);
        player.Hand.AddInternal(anger);
        Assert.False(Hook.ShouldPlay(cs, anger, out var preventer, AutoPlayType.None), "manual play is blocked");
        Assert.True(ReferenceEquals(preventer, enthralled.Listener), "the Enthralled listener is the preventer");
        Assert.True(Hook.ShouldPlay(cs, anger, out _, AutoPlayType.Default), "auto-play is allowed");
        Assert.True(Hook.ShouldPlay(cs, enthralled, out _, AutoPlayType.None), "Enthralled itself stays playable");
        player.Hand.RemoveInternal(enthralled);
        player.DiscardPile.AddInternal(enthralled);
        Assert.True(Hook.ShouldPlay(cs, anger, out _, AutoPlayType.None), "outside the hand it has no effect");
    }

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        player.Energy = 3;
        var cs = new CombatState(player, "CONTENT_CARDS_3_1", 0, new RunRngSet(new Rng(1UL)));
        cs.NextCreatureId = 1;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddMonster(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), DummyMonsterDef, hp, hp, null, null);
        var idle = new MoveStateDef("CONTENT_CARDS_3_1_IDLE", static (_, _, _) => { });
        monster.InitAi(new MonsterAiRuntime(new MonsterMoveStateMachineDef(new AiStateDef[] { idle }, idle)));
        cs.Monsters.Add(monster);
        return monster;
    }

    private sealed class DummyMonster : MonsterDef
    {
        public DummyMonster()
            : base("CONTENT_CARDS_3_1_DUMMY")
        {
        }

        public override int MinInitialHp(CombatState cs) => 40;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }
}
