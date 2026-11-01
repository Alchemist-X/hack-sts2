using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class KernelGapsW35Tests
{
    private const string Encounter = "NIBBITS_WEAK";

    private static string[] Repeat(string id, int n)
    {
        var a = new string[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = id;
        }
        return a;
    }

    private static int HandIndexOf(CombatState cs, string id)
    {
        var hand = cs.Player.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    public static void Test_KernelGaps_VoidFormEndsTheTurnAfterThePlayCompletes()
    {
        var cs = CombatLoop.CreateCombat(5UL, Repeat(VoidForm.CardId, 10), Encounter, 0);
        Assert.Equal(1, cs.RoundNumber);
        Assert.Equal(CombatSide.Player, cs.CurrentSide);
        int index = HandIndexOf(cs, VoidForm.CardId);
        Assert.True(index >= 0, "a void form is in hand");
        CombatLoop.Step(cs, GameAction.Play(index));
        if (cs.IsInProgress)
        {
            Assert.Equal(2, cs.RoundNumber, "the turn ended and the next player turn began");
            Assert.Equal(CombatSide.Player, cs.CurrentSide);
            Assert.Equal(0, cs.PlayersReadyToEndTurn.Count, "the ready set is cleared at the player turn start");
            bool hasPower = false;
            for (int i = 0; i < cs.Player.Powers.Count; i++)
            {
                hasPower |= cs.Player.Powers[i].Id == VoidForm.VoidFormPowerId;
            }
            Assert.True(hasPower, "void form power applied before the turn ended");
        }
    }

    public static void Test_KernelGaps_EndTurnMarksThePlayerReadyAndCloneHashesTheSet()
    {
        var cs = CombatLoop.CreateCombat(5UL, Repeat("STRIKE_IRONCLAD", 10), Encounter, 0);
        PlayerCmd.EndTurn(cs, cs.Player, canBackOut: true);
        Assert.True(PlayerCmd.IsPlayerReadyToEndTurn(cs, cs.Player));
        Assert.True(cs.AllPlayersReadyToEndTurn());
        var clone = cs.DeepClone();
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        PlayerCmd.UndoReadyToEndTurn(cs, cs.Player);
        Assert.False(cs.AllPlayersReadyToEndTurn());
        Assert.NotEqual(StateHasher.Hash(cs), StateHasher.Hash(clone));
        Assert.True(clone.AllPlayersReadyToEndTurn(), "the clone keeps its own ready set");
    }

    public static void Test_KernelGaps_EscapeListsTheCreatureAndScalesBaseGoldProportion()
    {
        var cs = CombatLoop.CreateCombat(5UL, Repeat("STRIKE_IRONCLAD", 10), Encounter, 0);
        var encounter = EncounterRegistry.Get(Encounter);
        Assert.True(cs.SpawnedEnemyIds.Count >= 1, "spawned enemy types recorded");
        Assert.Equal(1f, encounter.CalculateGoldProportion(cs));
        var m = cs.Monsters[0];
        var clone = cs.DeepClone();
        CreatureCmd.Escape(cs, m);
        Assert.Equal(1, cs.EscapedCreatures.Count);
        Assert.False(cs.Monsters.Contains(m));
        Assert.Equal(1f - 1f / cs.SpawnedEnemyIds.Count, encounter.CalculateGoldProportion(cs));
        Assert.NotEqual(StateHasher.Hash(cs), StateHasher.Hash(clone));
        var again = cs.DeepClone();
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(again));
        Assert.Equal(1, again.EscapedCreatures.Count);
        Assert.False(ReferenceEquals(again.EscapedCreatures[0], cs.EscapedCreatures[0]), "escaped creatures are deep-cloned");
    }

    public static void Test_KernelGaps_GremlinMercGoldProportionReadsEscapesAndTheft()
    {
        var def = new GremlinMercNormal();
        var cs = CombatLoop.CreateCombat(5UL, Repeat("STRIKE_IRONCLAD", 10), Encounter, 0);
        cs.EncounterExtra = def.CreateExtraState();
        Assert.Equal(1f, def.CalculateGoldProportion(cs));
        var fat = MonsterSpawner.SpawnForAdd(cs, MonsterRegistry.Get(GremlinMercNormal.FatGremlinId), CombatSide.Enemy, null);
        CreatureCmd.Escape(cs, fat);
        Assert.Equal(0.5f, def.CalculateGoldProportion(cs));
        var before = StateHasher.Hash(cs);
        GremlinMercNormal.MarkGoldStolen(cs);
        Assert.True(GremlinMercNormal.GoldWasStolen(cs));
        Assert.Equal(0f, def.CalculateGoldProportion(cs));
        Assert.NotEqual(before, StateHasher.Hash(cs));
        var clone = cs.DeepClone();
        Assert.True(GremlinMercNormal.GoldWasStolen(clone));
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
    }

    public static void Test_KernelGaps_EncounterExtraIsCreatedAtSetUp()
    {
        var cs = CombatLoop.CreateCombat(5UL, Repeat("STRIKE_IRONCLAD", 10), BattlewornDummyEventV1Encounter.EncounterIdEntry, 0);
        Assert.True(cs.EncounterExtra is BattlewornDummyEventEncounter.Extra);
        Assert.True(BattlewornDummyEventEncounter.IsBattlewornDummyEncounter(cs));
        Assert.False(BattlewornDummyEventEncounter.GetRanOutOfTime(cs));
        BattlewornDummyEventEncounter.SetRanOutOfTime(cs, true);
        Assert.True(BattlewornDummyEventEncounter.GetRanOutOfTime(cs.DeepClone()));
    }
}
