using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Diff;
using Sts2Sim.Diff.Run;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

/// <summary>W4.1 run-fixes regressions (sim/specs/diff-report-w4-run.md, section W4.1), each from a minimal repro.</summary>
public static class RunFixesW41Tests
{
    // ---- T1 event pulls ------------------------------------------------------------------------------------------

    public static void Test_RunFixesW41_UnportedCrystalSphereIsSkippedInAct1()
    {
        // Seed G0G2JMFEYHRS (IRONCLAD A10): the act-1 pool holds CRYSTAL_SPHERE at index 2, SUNKEN_STATUE at 3; the
        // recorded run pulled SUNKEN_STATUE on floor 3. Models.Events/CrystalSphere.cs:236-243 (gold >= 100, act > 0).
        var sim = RunSim.NewRun(new RunConfig("G0G2JMFEYHRS", StartingDecks.IroncladCharacterId, 10));
        var rs = sim.State;
        var rooms = rs.Acts[0].Rooms.DeepClone();
        Assert.Equal(UnportedEventRules.CrystalSphereId, rooms.events[2]);
        Assert.False(EventRegistry.Models.Contains(UnportedEventRules.CrystalSphereId), "CRYSTAL_SPHERE options stay unported");
        rooms.eventsVisited = 2;
        rs.Players[0].Gold = 300;
        rooms.EnsureNextEventIsValid(rs);
        Assert.Equal("SUNKEN_STATUE", rooms.NextEventId, "act 0: CRYSTAL_SPHERE is not allowed");
        rooms.eventsVisited = 2;
        rs.CurrentActIndex = 1;
        rooms.EnsureNextEventIsValid(rs);
        Assert.Equal(UnportedEventRules.CrystalSphereId, rooms.NextEventId, "act 1 with 300 gold: allowed");
        rooms.eventsVisited = 2;
        rs.Players[0].Gold = 99;
        rooms.EnsureNextEventIsValid(rs);
        Assert.Equal("SUNKEN_STATUE", rooms.NextEventId, "99 gold: not allowed");
    }

    public static void Test_RunFixesW41_UnportedEventPullBecomesStubInsteadOfThrowing()
    {
        // map_move into CRYSTAL_SPHERE threw KeyNotFoundException (G3 G0G2JMFEYHRS before the eligibility fix).
        var sim = RunSim.NewRun(new RunConfig("G0G2JMFEYHRS", StartingDecks.IroncladCharacterId, 10));
        var rs = sim.State;
        var act = rs.Acts[0];
        act.Rooms.eventsVisited = 2;
        rs.CurrentActIndex = 1;
        rs.Players[0].Gold = 300;
        var ev = act.PullNextEvent(rs);
        Assert.True(ev is SimStubEvent, ev.GetType().Name);
        Assert.Equal(UnportedEventRules.CrystalSphereId, ev.Id);
        Assert.True(rs.VisitedEventIds.Contains(UnportedEventRules.CrystalSphereId));
    }

    public static void Test_RunFixesW41_FakeMerchantEligibility()
    {
        // Models.Events/FakeMerchant.cs:194-205: act >= 1, single player, gold >= 100 or a FOUL_POTION.
        var sim = RunSim.NewRun(new RunConfig("G0G2JMFEYHRS", StartingDecks.IroncladCharacterId, 10));
        var rs = sim.State;
        rs.Players[0].Gold = 150;
        Assert.False(UnportedEventRules.IsAllowed(UnportedEventRules.FakeMerchantId, rs), "act 0");
        rs.CurrentActIndex = 1;
        Assert.True(UnportedEventRules.IsAllowed(UnportedEventRules.FakeMerchantId, rs));
        rs.Players[0].Gold = 10;
        Assert.False(UnportedEventRules.IsAllowed(UnportedEventRules.FakeMerchantId, rs));
        rs.Players[0].AddPotionInternal(PotionRegistry.Create(UnportedEventRules.FoulPotionId));
        Assert.True(UnportedEventRules.IsAllowed(UnportedEventRules.FakeMerchantId, rs), "a FOUL_POTION is enough");
    }

    private static string FuturePotionsHistory() => """
        {"schema_version":10,"build_id":"v0.111.0","game_mode":"standard","seed":"Q3X6PF8S37GA","ascension":5,"modifiers":[],
         "acts":["ACT.OVERGROWTH","ACT.HIVE","ACT.GLORY"],"players":[{"character":"CHARACTER.SILENT","relics":[],"deck":[]}],
         "map_point_history":[[
          {"map_point_type":"ancient","rooms":[{"model_id":"EVENT.NEOW","room_type":"event"}],
           "player_stats":[{"current_gold":99,"current_hp":56,"max_hp":70,"relic_choices":[{"choice":"RELIC.SILVER_CRUCIBLE","was_picked":true}]}]},
          {"map_point_type":"monster","rooms":[{"model_id":"ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK","room_type":"monster","monster_ids":["MONSTER.FUZZY_WURM_CRAWLER"]}],
           "player_stats":[{"current_gold":114,"current_hp":50,"max_hp":70,"potion_choices":[{"choice":"POTION.BLESSING_OF_THE_FORGE","was_picked":true}]}]},
          {"map_point_type":"monster","rooms":[{"model_id":"ENCOUNTER.NIBBITS_WEAK","room_type":"monster","monster_ids":["MONSTER.NIBBIT"]}],
           "player_stats":[{"current_gold":125,"current_hp":49,"max_hp":70,"potion_choices":[{"choice":"POTION.POTION_OF_BINDING","was_picked":true}]}]},
          {"map_point_type":"unknown","rooms":[{"model_id":"EVENT.THE_FUTURE_OF_POTIONS","room_type":"event"}],
           "player_stats":[{"current_gold":125,"current_hp":49,"max_hp":70,"potion_discarded":["POTION.BLESSING_OF_THE_FORGE"]}]}
         ]]}
        """;

    public static void Test_RunFixesW41_EventPullOracleReplaysPotionsFromPlayerStats()
    {
        // .run 1793233995 floors 1-4: two potions picked on floors 2-3 make THE_FUTURE_OF_POTIONS (needs >= 2 potions,
        // Models.Events/TheFutureOfPotions.cs:351-354) the floor-4 pull; without the replay the oracle pulled
        // TABLET_OF_TRUTH.
        var record = RunRecordReader.ReadHistory(FuturePotionsHistory(), "memory://1793233995-prefix.run");
        var stats = Assert.NotNull(record.Floors[0][1].Stats);
        Assert.SequenceEqual(new[] { "BLESSING_OF_THE_FORGE" }, stats.PotionsGained);
        var checks = RoomPullOracle.Check(record, new RunOracleOptions());
        var events = checks.Single(c => c.Oracle == RunOracleNames.EventPulls);
        Assert.Equal(RunOracleStatus.Agree, events.Status, events.Detail);
        Assert.Equal(2, events.Compared, "the Neow ancient and the floor-4 pull");
    }

    // ---- G3 run-layer first divergences -------------------------------------------------------------------------

    public static void Test_RunFixesW41_LaggingRelicListIsRecorderGapOnlyForThisFloorsPicks()
    {
        // G1YFC196B2H2 floor 4 ROOM_FULL_OF_CHEESE: the event_proceed pre_state lacks CHOSEN_CHEESE, which SEARCH
        // obtained synchronously (Commands/RelicCmd.cs:403-410) before SetEventFinished.
        var oracle = JsonNode.Parse("""{"player":{"relics":[{"id":"RING_OF_THE_SNAKE"},{"id":"KALEIDOSCOPE"}]}}""")!.AsObject();
        var sim = JsonNode.Parse("""{"player":{"relics":[{"id":"RING_OF_THE_SNAKE"},{"id":"KALEIDOSCOPE"},{"id":"CHOSEN_CHEESE"}]}}""")!.AsObject();
        Assert.True(G3Loop.IsLaggingRelicList(oracle, sim, new[] { "CHOSEN_CHEESE" }));
        Assert.False(G3Loop.IsLaggingRelicList(oracle, sim, Array.Empty<string>()), "a relic not picked on this floor is a real divergence");
        Assert.False(G3Loop.IsLaggingRelicList(oracle, sim, new[] { "KALEIDOSCOPE" }));
        var swapped = JsonNode.Parse("""{"player":{"relics":[{"id":"KALEIDOSCOPE"},{"id":"RING_OF_THE_SNAKE"},{"id":"CHOSEN_CHEESE"}]}}""")!.AsObject();
        Assert.False(G3Loop.IsLaggingRelicList(oracle, swapped, new[] { "CHOSEN_CHEESE" }), "order of the shared relics must agree");
    }

    public static void Test_RunFixesW41_HandSelectCandidatesFollowTheHandOrder()
    {
        // NBNDT3S61AUM floor 8 FOSSIL_STALKER_NORMAL decision 19 (DUAL_WIELD OnPlay): the recorder lists the
        // candidates in UI holder order (STRIKE_SILENT, NEUTRALIZE); the model hand is DEFEND, NEUTRALIZE, STRIKE, SURVIVOR.
        var d = JsonNode.Parse("""
            {"pre_state":{"player":{"hand":[{"id":"DEFEND_SILENT","upgraded":false},{"id":"NEUTRALIZE","upgraded":false},{"id":"STRIKE_SILENT","upgraded":false},{"id":"SURVIVOR","upgraded":false}]},
                          "select":{"kind":"hand_select","candidates":[{"id":"STRIKE_SILENT","upgraded":false},{"id":"NEUTRALIZE","upgraded":false}]}},
             "action":{"type":"choose","kind":"hand","picks":[{"index":0,"card_id":"STRIKE_SILENT","upgraded":false}]}}
            """)!.AsObject();
        Assert.True(G3Replay.ReorderHandSelect(d));
        var cands = d["pre_state"]!["select"]!["candidates"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ToList();
        Assert.SequenceEqual(new[] { "NEUTRALIZE", "STRIKE_SILENT" }, cands);
        Assert.Equal(1, d["action"]!["picks"]![0]!["index"]!.GetValue<int>(), "the pick follows its card");
        Assert.False(G3Replay.ReorderHandSelect(d), "already in hand order");
    }

    // ---- v2 reader: unavailable fields -------------------------------------------------------------------------

    public static void Test_RunFixesW41_ReaderReadsNullDeckAndActionAsUnavailable()
    {
        var bundle = JsonNode.Parse("""
            {"bundle_version":2,"kind":"run","source":{"lineage":"W41"},"game_version":"v0.111.0","rules_version":"v0.111.0",
             "seeding":"xxh64","seed":"W41UNAVAIL01","character":"IRONCLAD","ascension":0,"game_mode":"standard","modifiers":[],"anchors":[],
             "segments":[
              {"id":"s1","kind":"combat","act":0,"floor":2,"room_type":"Monster","room_model_id":"ENCOUNTER.NIBBITS_WEAK",
               "start":{"encounter_id":"NIBBITS_WEAK","player":{"hp":70,"max_hp":80},"deck":null,"relics":[]},
               "decisions":[{"index":0,"screen":"combat","pre_state":null,"action":null}],
               "eligibility":{"eligible":false,"reasons":["recorder_gap:deck_order","decision_unresolved"]}}]}
            """)!;
        var run = RunBundleJson.Parse(bundle.ToJsonString(), "memory://w41.json");
        var segment = run.Segments.Single();
        Assert.Null(segment.FormatError);
        Assert.SequenceEqual(new[] { "start.deck", "decisions[0].action" }, segment.UnavailableFields);
        Assert.Equal(V2Action.Unavailable, segment.Decisions[0].Action.Type);
        bundle["segments"]![0]!["eligibility"]!["eligible"] = true;
        Assert.Throws<BundleFormatException>(() => RunBundleJson.Parse(bundle.ToJsonString(), "memory://w41-eligible.json"), "an eligible segment must carry them");
    }

    // ---- [SavedProperty] -----------------------------------------------------------------------------------------

    public static void Test_RunFixesW41_SavedPropertiesRoundTripThroughTheRegistry()
    {
        // GIRYA.TimesLifted (Models.Relics/Girya.cs), SPOILS_MAP.SpoilsActIndex (Models.Cards/SpoilsMap.cs) carry no
        // [SavedProperty] in the sim defs; the registry maps them.
        var girya = RelicRegistry.Create("GIRYA");
        RunImporter.FillProperties(girya, new SavedProperties { Ints = { new("TimesLifted", 2) } });
        Assert.Equal(2, (int)SavedPropertyRegistry.Member(girya.GetType(), "TimesLifted")!.GetValue(girya)!);
        var exported = Assert.NotNull(RunExporter.ToSavedProperties(girya));
        Assert.SequenceEqual(new[] { new KeyValuePair<string, int>("TimesLifted", 2) }, exported.Ints);
        Assert.Equal(0, exported.Bools.Count, "IsWax/IsMelted are SaveIfNotTypeDefault (Models/RelicModel.cs:252-277)");

        var spoils = new CardInstance(CardRegistry.Get("SPOILS_MAP"), 0);
        var extra = Assert.NotNull(spoils.Extra);
        RunImporter.FillProperties(extra, new SavedProperties { Ints = { new("SpoilsActIndex", 1) } });
        var cardProps = Assert.NotNull(RunExporter.ToSavedProperties(extra));
        Assert.SequenceEqual(new[] { new KeyValuePair<string, int>("SpoilsActIndex", 1) }, cardProps.Ints);
        Assert.Equal(0, RunImporter.MissingProperties(extra.GetType(), cardProps).Count);
    }

    public static void Test_RunFixesW41_CosmeticPetSkinSavedPropertiesMap()
    {
        // BYRDPIP / PAELS_LEGION [SavedProperty] string Skin (Models.Relics/Byrdpip.cs, PaelsLegion.cs): cosmetic (rolled
        // on pickup from a model-seeded Rng), but a save holding it was unsupported ("saved properties Skin are not
        // mapped") until the sim defs held it.
        foreach (var (id, skin) in new[] { ("BYRDPIP", "version3"), ("PAELS_LEGION", "spikes") })
        {
            var relic = RelicRegistry.Create(id);
            var props = new SavedProperties { Strings = { new("Skin", skin) } };
            Assert.Equal(0, RunImporter.MissingProperties(relic.GetType(), props).Count, id);
            RunImporter.FillProperties(relic, props);
            var exported = Assert.NotNull(RunExporter.ToSavedProperties(relic));
            Assert.SequenceEqual(new[] { new KeyValuePair<string, string>("Skin", skin) }, exported.Strings);
        }
    }

    public static void Test_RunFixesW41_FixtureSaveWithGiryaAndSpoilsMapIsSupported()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "run_save_a8_silent_boss1.save");
        if (!File.Exists(path))
        {
            path = Path.Combine(Path.GetDirectoryName(typeof(RunFixesW41Tests).Assembly.Location)!, "Fixtures", "run_save_a8_silent_boss1.save");
        }
        var save = RunSaveReader.ReadFile(path);
        Assert.True(save.Players[0].Relics.Any(r => r.Id == "GIRYA" && r.Props != null), "fixture holds GIRYA with props");
        Assert.True(save.Players[0].Deck.Any(c => c.Id == "SPOILS_MAP" && c.Props != null), "fixture holds SPOILS_MAP with props");
        var reasons = RunImporter.CheckSupported(save, ImportOptions.Default);
        Assert.Equal(0, reasons.Count, string.Join("; ", reasons));
        var sim = RunSim.FromSave(save, ImportOptions.Default);
        var back = RunExporter.ToSave(sim.State, RunExporter.PreFinishedRoomOf(sim.State));
        var girya = back.Players[0].Relics.Single(r => r.Id == "GIRYA");
        var savedGirya = save.Players[0].Relics.Single(r => r.Id == "GIRYA");
        Assert.SequenceEqual(savedGirya.Props!.Ints, Assert.NotNull(girya.Props).Ints);
        var spoils = back.Players[0].Deck.First(c => c.Id == "SPOILS_MAP");
        var savedSpoils = save.Players[0].Deck.First(c => c.Id == "SPOILS_MAP");
        Assert.SequenceEqual(savedSpoils.Props!.Ints, Assert.NotNull(spoils.Props).Ints);
    }

    public static void Test_RunFixesW41_PreFinishedBattlewornDummyLoadsRanOutOfTime()
    {
        // Rooms/CombatRoom.cs:685-686 LoadCustomState; Models.Encounters/BattlewornDummyEventEncounter.cs:24-32.
        var encounterId = EncounterRegistry.Defs.AllIds.First(id => EncounterRegistry.Get(id) is BattlewornDummyEventEncounter);
        var sim = RunSim.NewRun(new RunConfig("G0G2JMFEYHRS", StartingDecks.IroncladCharacterId, 10));
        var saved = new SavedRoom
        {
            RoomType = RoomType.Monster,
            EncounterId = encounterId,
            IsPreFinished = true,
            EncounterState = new Dictionary<string, string>(StringComparer.Ordinal) { [EncounterCustomState.RanOutOfTimeKey] = bool.TrueString }
        };
        var room = Assert.NotNull(RunImporter.CreatePreFinishedRoom(sim.State, saved) as CombatRoom);
        var extra = Assert.NotNull(room.EncounterExtra as BattlewornDummyEventEncounter.Extra);
        Assert.True(extra.RanOutOfTime);
        Assert.Equal(bool.TrueString, EncounterCustomState.Save(room.EncounterExtra)[EncounterCustomState.RanOutOfTimeKey]);
    }
}
