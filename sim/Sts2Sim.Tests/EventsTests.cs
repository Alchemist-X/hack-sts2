using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class EventsTests
{
    private static RunState NewRun(string seed, int ascension = 0)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), ascension);
        foreach (var id in StartingDecks.Ironclad)
        {
            var card = new CardInstance(CardRegistry.Get(id), rs.AllocateCardInstanceId());
            card.AttachTo(player);
            player.Deck.Add(card);
        }
        return rs;
    }

    private static EventModel Begin(RunState rs, string eventId, bool isPreFinished = false)
    {
        var ev = EventRegistry.Create(eventId);
        ev.BeginEvent(rs, rs.Players[0], isPreFinished);
        return ev;
    }

    private static string[] Keys(EventModel ev) => ev.CurrentOptions.Select(o => o.RelicId ?? o.TextKey).ToArray();

    public static void Test_Events_AncientsAreRegistered()
    {
        foreach (var id in new[] { "NEOW", "OROBAS", "PAEL", "TEZCATARA", "NONUPEIPE", "TANX", "VAKUU", "DARV", "THE_ARCHITECT" })
        {
            Assert.True(EventRegistry.Models.Contains(id), id + " is registered");
            Assert.Equal(id, EventRegistry.Create(id).Id);
        }
        Assert.Equal("NEOW", DeterministicHash.Slugify(nameof(Neow)));
        Assert.Equal("THE_ARCHITECT", DeterministicHash.Slugify(nameof(TheArchitect)));
        Assert.Equal("BIIIG_HUG", DeterministicHash.Slugify("BiiigHug"));
        Assert.Equal("TANXS_WHISTLE", DeterministicHash.Slugify("TanxsWhistle"));
    }

    public static void Test_Events_NeowOptionsOnSeeds()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in new[] { "SEED0", "ABCDEF123456", "1", "NEOWTEST", "Z9Z9Z9", "QWERTY" })
        {
            var rs = NewRun(seed);
            var ev = Begin(rs, Neow.EventId);
            var keys = Keys(ev);
            Assert.Equal(3, keys.Length, seed + ": Neow offers three options");
            Assert.True(Neow.CurseRelicIds.Contains(keys[2]), seed + ": the third option is the curse");
            Assert.False(Neow.CurseRelicIds.Contains(keys[0]) || Neow.CurseRelicIds.Contains(keys[1]), seed + ": the first two are positive");
            Assert.NotEqual(keys[0], keys[1], seed);
            Assert.False(keys.Contains("MASSIVE_SCROLL"), seed + ": MassiveScroll is multiplayer only");
            if (keys[2] == "CURSED_PEARL")
            {
                Assert.False(keys.Contains("GOLDEN_PEARL"), seed);
            }
            if (keys[2] == "NEOWS_SACRIFICE")
            {
                Assert.False(keys.Contains("PHIAL_HOLSTER") || keys.Contains("LOST_COFFER"), seed);
            }
            Assert.Equal(StartingDecks.IroncladStartingHp, rs.Players[0].CurrentHp, seed + ": Neow arrival heals to full at A0");
            Assert.SequenceEqual(keys, Keys(Begin(NewRun(seed), Neow.EventId)), seed + ": options depend only on the seed");
            Assert.True(ev.CurrentOptions[0].TextKey.StartsWith("NEOW.pages.INITIAL.options.", StringComparison.Ordinal));
            foreach (var k in keys)
            {
                seen.Add(k);
            }
        }
        Assert.True(seen.Count > 4, "different seeds give different Neow options");
    }

    public static void Test_Events_NeowArrivalHealAtWearyTraveler()
    {
        var rs = NewRun("WEARY", (int)AscensionLevel.WearyTraveler);
        var ev = (Neow)Begin(rs, Neow.EventId);
        int expected = StartingDecks.HpAfterNeowArrival(StartingDecks.IroncladStartingHp, (int)AscensionLevel.WearyTraveler);
        Assert.Equal(expected, rs.Players[0].CurrentHp);
        Assert.Equal(expected, ev.HealedAmount);
    }

    public static void Test_Events_NeowSeedUsesEventPrivateRng()
    {
        var rs = NewRun("PRIVATE");
        var before = StateHasher.Hash(rs.Rng);
        var ev = Begin(rs, Neow.EventId);
        Assert.Equal(before, StateHasher.Hash(rs.Rng), "Neow option generation leaves the run streams untouched");
        var expectedSeed = RngSeeds.Event(rs.Rng.Seed, false, 0, Neow.EventId);
        Assert.Equal(unchecked(rs.Rng.Seed + DeterministicHash.GetDeterministicHashCode("NEOW")), expectedSeed);
        Assert.True(ev.Rng!.Counter > 0, "the private rng was consumed");
    }

    public static void Test_Events_AncientOptionRegeneration()
    {
        foreach (var id in new[] { "OROBAS", "PAEL", "TEZCATARA", "NONUPEIPE", "TANX", "VAKUU", "DARV" })
        {
            foreach (var seed in new[] { "ANC1", "ANC2", "ANC3" })
            {
                var rs = NewRun(seed);
                rs.Players[0].CurrentHp = 40;
                var first = Begin(rs, id);
                var keys = Keys(first);
                Assert.Equal(3, keys.Length, id + "/" + seed);
                Assert.Equal(StartingDecks.IroncladStartingHp, rs.Players[0].CurrentHp, id + ": arrival heal to full at A0");
                var again = Begin(rs, id);
                Assert.SequenceEqual(keys, Keys(again), id + "/" + seed + ": a fresh private rng regenerates the same options");
                var preFinished = Begin(rs, id, isPreFinished: true);
                Assert.True(preFinished.IsFinished, id + ": a pre-finished load starts finished");
                Assert.Equal(0, preFinished.CurrentOptions.Count, id);
                Assert.Equal(first.Rng!.Counter, preFinished.Rng!.Counter, id + ": the pre-finished load regenerates options on its private rng");
            }
        }
    }

    public static void Test_Events_AncientRoomChooseFinishesAndMarksPreFinished()
    {
        var rs = NewRun("ROOM");
        var room = new EventRoom(EventRegistry.Create(Vakuu.EventId));
        room.Enter(rs);
        var ev = Assert.NotNull(room.LocalMutableEvent);
        Assert.False(room.IsPreFinished);
        Assert.Equal(0, room.Id);
        Assert.Equal(1, rs.NextRoomId);
        var option = ev.CurrentOptions[1];
        int relicsBefore = rs.Players[0].Relics.Count;
        ev.ChooseOption(rs, 1);
        Assert.True(option.WasChosen);
        Assert.True(ev.IsFinished);
        Assert.True(room.IsPreFinished, "a finished ancient marks its room pre-finished (save point C)");
        int expectedRelics = relicsBefore + (option.Relic != null ? 1 : 0);
        Assert.Equal(expectedRelics, rs.Players[0].Relics.Count);
        Assert.Throws<InvalidOperationException>(() => ev.ChooseOption(rs, 0));
    }

    public static void Test_Events_EventRoomCloneIsIndependent()
    {
        var rs = NewRun("CLONE");
        var room = new EventRoom(EventRegistry.Create(Tanx.EventId));
        room.Enter(rs);
        rs.CurrentRooms.Add(room);
        var clone = rs.DeepClone();
        Assert.Equal(StateHasher.Hash(room), StateHasher.Hash(clone.CurrentRooms[0]));
        var clonedRoom = (EventRoom)clone.CurrentRooms[0];
        clone.Choice.Answerer = new Content_relics_9_1Tests.PickFirst();
        clonedRoom.LocalMutableEvent!.ChooseOption(clone, 0);
        Assert.True(clonedRoom.LocalMutableEvent.IsFinished);
        Assert.True(clonedRoom.IsPreFinished);
        Assert.False(room.LocalMutableEvent!.IsFinished, "choosing on the clone leaves the source untouched");
        Assert.False(room.IsPreFinished);
        Assert.True(ReferenceEquals(clonedRoom.LocalMutableEvent.Owner, clone.Players[0]));
    }

    public static void Test_Events_TheArchitectWinsRun()
    {
        var rs = NewRun("ARCH");
        var ev = (TheArchitect)Begin(rs, TheArchitect.EventId);
        Assert.Equal(1, ev.CurrentOptions.Count);
        ev.ChooseOption(rs, 0);
        Assert.True(ev.RunWon);
        Assert.True(ev.IsFinished);
    }

    public static void Test_Events_StubEventProceeds()
    {
        var rs = NewRun("STUB");
        var ev = SimStubEvent.CreateOrStub("SOME_UNPORTED_EVENT");
        Assert.True(ev is SimStubEvent);
        ev.BeginEvent(rs, rs.Players[0], false);
        Assert.True(ev.CurrentOptions[0].IsProceed);
        ev.ChooseOption(rs, 0);
        Assert.True(ev.IsFinished);
    }

    public static void Test_Events_OptionLegality()
    {
        var locked = new EventOption("LOCKED", null);
        Assert.True(locked.IsLocked);
        Assert.False(locked.IsEnabled);
        int calls = 0;
        var once = new EventOption("ONCE", _ => calls++);
        var rs = NewRun("OPT");
        once.Chosen(rs);
        once.Chosen(rs);
        Assert.Equal(1, calls, "options disable on chosen by default (Events/EventOption.cs:280)");
        Assert.False(once.IsEnabled);
        var repeat = new EventOption("REPEAT", _ => calls++, disableOnChosen: false);
        repeat.Chosen(rs);
        repeat.Chosen(rs);
        Assert.Equal(3, calls);
    }

    public static void Test_Events_EventCombatGoesThroughRoomStack()
    {
        var rs = NewRun("COMBAT");
        rs.Acts.Add(new ActInstance(ActModel.ActsByIndex[0][0]));
        var ev = new SharedCombatEvent();
        ev.BeginEvent(rs, rs.Players[0], false);
        ev.ChooseOption(rs, 0);
        var room = Assert.NotNull(RoomStack.CurrentRoom(rs) as CombatRoom);
        Assert.Equal("SHARED_COMBAT_EVENT", room.ParentEventId);
        Assert.True(room.ShouldResumeParentEventAfterCombat);
        Assert.NotNull(rs.Combat);
    }

    [SimExclude]
    private sealed class SharedCombatEvent : EventModel
    {
        public override string Id => "SHARED_COMBAT_EVENT";

        public override bool IsShared => true;

        protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
            => new[] { new EventOption(InitialOptionKey("FIGHT"), Fight) };

        private void Fight(RunState rs)
        {
            var encounter = EncounterRegistry.Get(EncounterRegistry.AllIds[0]);
            EnterCombatWithoutExitingEvent(rs, encounter, Array.Empty<Reward>(), shouldResumeAfterCombat: true);
        }
    }
}
