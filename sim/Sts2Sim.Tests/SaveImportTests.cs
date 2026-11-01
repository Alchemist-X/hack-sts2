using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Tests;

public static class SaveImportTests
{
    private const string FixtureName = "run_save_a8_silent_boss1.save";

    private static string FixturePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", FixtureName);

    private static string FixtureJson() => File.ReadAllText(FixturePath());

    private static SavedRun Fixture() => RunSaveReader.ReadFile(FixturePath());

    private static SavedRun Edit(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(FixtureJson())!.AsObject();
        edit(root);
        return RunSaveReader.Read(root.ToJsonString());
    }

    private static SavedRun PortedOnlyFixture() => Edit(KeepPortedOnly);

    private static void KeepPortedOnly(JsonObject root)
    {
        var player = root["players"]![0]!.AsObject();
        var deck = player["deck"]!.AsArray();
        for (int i = deck.Count - 1; i >= 0; i--)
        {
            var id = RunSaveReader.StripCategory((string)deck[i]!["id"]!);
            bool ported = CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef && deck[i]!["props"] == null;
            var ench = deck[i]!["enchantment"];
            if (ench != null && !EnchantmentRegistry.Models.Contains(RunSaveReader.StripCategory((string)ench["id"]!)))
            {
                ported = false;
            }
            if (!ported)
            {
                deck.RemoveAt(i);
            }
        }
        var relics = player["relics"]!.AsArray();
        for (int i = relics.Count - 1; i >= 0; i--)
        {
            var id = RunSaveReader.StripCategory((string)relics[i]!["id"]!);
            if (!RelicRegistry.Models.Contains(id) || relics[i]!["props"] != null)
            {
                relics.RemoveAt(i);
            }
        }
    }

    public static void Test_SaveReader_ParsesFixtureFields()
    {
        var save = Fixture();
        Assert.Equal(20, save.SchemaVersion);
        Assert.Equal("G1YFC196B2H2", save.Rng.Seed);
        Assert.Equal(8, save.Ascension);
        Assert.Equal("standard", save.GameMode);
        Assert.Equal(0, save.CurrentActIndex);
        Assert.SequenceEqual(new[] { "UNDERDOCKS", "HIVE", "GLORY" }, save.Acts.Select(a => a.Id));
        Assert.Equal(17, save.VisitedMapCoords.Count);
        Assert.Equal(new MapCoord(3, 0), save.VisitedMapCoords[0]);
        Assert.SequenceEqual(new[] { "ROOM_FULL_OF_CHEESE", "THE_LEGENDS_WERE_TRUE", "SELF_HELP_BOOK" }, save.EventsSeen.Select(RunSaveReader.StripCategory));
        Assert.Equal(12, save.Rng.Rngs.Count);
        Assert.Equal(new RngSnapshot(407, 13230861342643808336uL, 16515130911208079210uL, 14573541431143797327uL, 7274832175699328883uL), save.Rng.Rngs["up_front"]);
        Assert.True(save.ExtraFields.StartedWithNeow);
        Assert.Equal(-5f, save.Odds.UnknownMapPointEliteOddsValue);
        Assert.Equal(0.5f, save.Odds.UnknownMapPointMonsterOddsValue);
        Assert.Equal(0.14999999f, save.Odds.UnknownMapPointShopOddsValue);
        Assert.Equal(0.02f, save.Odds.UnknownMapPointTreasureOddsValue);
        Assert.Equal(6, save.SharedRelicGrabBag.RelicIdLists.Count);
        Assert.Equal(30, save.SharedRelicGrabBag.RelicIdLists[RelicRarity.Uncommon].Count);
        Assert.Equal(2, save.SharedRelicGrabBag.RelicIdLists[RelicRarity.Ancient].Count);

        var act = save.Acts[0];
        Assert.Equal(15, act.Rooms.NormalEncounterIds.Count);
        Assert.Equal("SLUDGE_SPINNER_WEAK", act.Rooms.NormalEncounterIds[0]);
        Assert.Equal(15, act.Rooms.EliteEncounterIds.Count);
        Assert.Equal(28, act.Rooms.EventIds.Count);
        Assert.Equal((4, 2, 7, 1), (act.Rooms.NormalEncountersVisited, act.Rooms.EliteEncountersVisited, act.Rooms.EventsVisited, act.Rooms.BossEncountersVisited));
        Assert.Equal("SOUL_FYSH_BOSS", act.Rooms.BossId);
        Assert.Null(act.Rooms.SecondBossId);
        Assert.Equal("NEOW", act.Rooms.AncientId);
        var map = Assert.NotNull(act.SavedMap);
        Assert.Equal((7, 16, 50), (map.Width, map.Height, map.Points.Count));
        Assert.Equal(MapPointType.Boss, map.Boss!.Type);
        Assert.Equal(new MapCoord(3, 16), map.Boss.Coord);
        Assert.Equal(MapPointType.Ancient, map.Start!.Type);
        Assert.SequenceEqual(new[] { new MapCoord(6, 1), new MapCoord(3, 1) }, map.StartCoords);
        Assert.Null(save.Acts[1].SavedMap);

        Assert.Equal(1, save.MapPointHistory.Count);
        var history = save.MapPointHistory[0];
        Assert.Equal(17, history.Count);
        Assert.Equal(MapPointType.Ancient, history[0].MapPointType);
        Assert.Equal("NEOW", history[0].Rooms[0].ModelId);
        Assert.Equal(RoomType.Boss, history[16].Rooms[0].RoomType);
        Assert.Equal(6, history[16].Rooms[0].TurnsTaken);
        Assert.SequenceEqual(new[] { "SOUL_FYSH" }, history[16].Rooms[0].MonsterIds);

        var pre = Assert.NotNull(save.PreFinishedRoom);
        Assert.Equal(RoomType.Boss, pre.RoomType);
        Assert.Equal("SOUL_FYSH_BOSS", pre.EncounterId);
        Assert.True(pre.IsPreFinished);
        Assert.Equal(1f, pre.GoldProportion);
        Assert.True(pre.ShouldResumeParentEvent);

        var player = save.Players[0];
        Assert.Equal("SILENT", player.CharacterId);
        Assert.Equal((28, 75, 3, 2, 278), (player.CurrentHp, player.MaxHp, player.MaxEnergy, player.MaxPotionSlotCount, player.Gold));
        Assert.Equal(22, player.Deck.Count);
        var taunt = player.Deck.Single(c => c.Id == "TAUNT");
        Assert.Equal(1, taunt.CurrentUpgradeLevel);
        Assert.Equal("NIMBLE", taunt.Enchantment!.Id);
        Assert.Equal(2, taunt.Enchantment.Amount);
        Assert.Equal(7, player.Relics.Count);
        var girya = player.Relics.Single(r => r.Id == "GIRYA");
        Assert.Equal(10, girya.FloorAddedToDeck);
        Assert.Equal(new KeyValuePair<string, int>("TimesLifted", 1), girya.Props!.Ints.Single());
        Assert.Equal(0, player.Potions.Count);
        Assert.Equal(7378453647942262644uL, player.Rng.Seed);
        Assert.Equal(96, player.Rng.Rngs["rewards"].Counter);
        Assert.Equal(-0.05f, player.Odds.CardRarityOddsValue);
        Assert.Equal(0.20000002f, player.Odds.PotionRewardOddsValue);
        Assert.Equal(4, player.RelicGrabBag.RelicIdLists.Count);
        Assert.Equal(163, player.UnlockState.NumberOfRuns);
        Assert.Equal(57, player.UnlockState.UnlockedEpochs.Count);
        Assert.True(player.UnlockState.EncountersSeen.All(id => !id.Contains('.')), "encounter ids lose their category");
    }

    public static void Test_SaveImport_EntryPointPerSavePoint()
    {
        Assert.Equal(SavePoint.PostCombatRewards, RunImporter.DetectEntryPoint(Fixture()));
        Assert.Equal(SavePoint.EnteringMapPoint, RunImporter.DetectEntryPoint(Edit(r => r.Remove("pre_finished_room"))));
        Assert.Equal(SavePoint.NoVisitedCoord, RunImporter.DetectEntryPoint(Edit(r =>
        {
            r.Remove("pre_finished_room");
            r["visited_map_coords"] = new JsonArray();
        })));
        Assert.Equal(SavePoint.AncientFinished, RunImporter.DetectEntryPoint(Edit(r =>
        {
            r["pre_finished_room"] = new JsonObject { ["room_type"] = "event", ["event_id"] = "EVENT.PAEL", ["is_pre_finished"] = true };
        })));
    }

    public static void Test_SaveImport_RejectsOtherSchemasAndModes()
    {
        var old = Edit(r => r["schema_version"] = 19);
        var reasons = RunImporter.CheckSupported(old, ImportOptions.Default);
        Assert.Equal(1, reasons.Count);
        Assert.True(reasons[0].StartsWith("schema_version 19", StringComparison.Ordinal), reasons[0]);
        var result = RunImporter.FromSave(old, ImportOptions.Default);
        Assert.False(result.IsSupported);
        Assert.Null(result.Sim);
        Assert.Null(result.EntryPoint);

        var custom = Edit(r =>
        {
            r["game_mode"] = "custom";
            r["modifiers"] = new JsonArray(new JsonObject { ["id"] = "MODIFIER.DRAFT" });
        });
        var customReasons = RunImporter.CheckSupported(custom, ImportOptions.Default);
        Assert.True(customReasons.Contains("game_mode custom is not standard"), string.Join("; ", customReasons));
        Assert.True(customReasons.Contains("modifier DRAFT is not supported"), string.Join("; ", customReasons));
    }

    public static void Test_SaveImport_ReportsUnportedContentInsteadOfThrowing()
    {
        var save = Fixture();
        var reasons = RunImporter.CheckSupported(save, ImportOptions.Default);
        if (!CardRegistry.TryGet("SPOILS_MAP", out var spoils) || spoils is UnportedCardDef)
        {
            Assert.True(reasons.Contains("card SPOILS_MAP is not ported"), string.Join("; ", reasons));
        }
        if (!RelicRegistry.Models.Contains("GIRYA"))
        {
            Assert.True(reasons.Contains("relic GIRYA is not ported"), string.Join("; ", reasons));
        }
        Assert.Equal(reasons.Count, reasons.Distinct(StringComparer.Ordinal).Count(), "reasons are deduplicated");
        if (reasons.Count > 0)
        {
            var result = RunImporter.FromSave(save, ImportOptions.Default);
            Assert.Null(result.Sim);
            Assert.Equal(SavePoint.PostCombatRewards, result.EntryPoint);
            Assert.SequenceEqual(reasons, result.UnsupportedReasons);
        }
    }

    public static void Test_SaveImport_CoreStateMatchesSave()
    {
        var save = PortedOnlyFixture();
        var saved = save.Players[0];
        var rs = RunImporter.ImportCore(save, ImportOptions.Default);
        var player = rs.Players[0];
        Assert.Equal("SILENT", player.CharacterId);
        Assert.Equal((28, 75, 3, 278), (player.CurrentHp, player.MaxHp, player.BaseMaxEnergy, player.Gold));
        Assert.Equal(2, player.MaxPotionCount, "the save's slot count already carries the A4 reduction");
        Assert.True(player.AscensionEffectsApplied);
        Assert.Equal(8, rs.Ascension);
        Assert.Equal(saved.Deck.Count, player.Deck.Count);
        for (int i = 0; i < saved.Deck.Count; i++)
        {
            Assert.Equal(saved.Deck[i].Id, player.Deck[i].Id);
            Assert.Equal(saved.Deck[i].CurrentUpgradeLevel, player.Deck[i].UpgradeLevel, saved.Deck[i].Id);
            Assert.Equal(saved.Deck[i].Enchantment?.Id, player.Deck[i].Enchantment?.Id, saved.Deck[i].Id);
            Assert.Equal(saved.Deck[i].Enchantment?.Amount ?? 0, player.Deck[i].Enchantment?.Amount ?? 0, saved.Deck[i].Id);
        }
        Assert.SequenceEqual(saved.Relics.Select(r => r.Id), player.Relics.Select(r => r.Id));
        Assert.SequenceEqual(saved.Relics.Select(r => r.FloorAddedToDeck ?? 0), player.Relics.Select(r => r.FloorAddedToDeck));
        Assert.Equal(407, rs.Rng.UpFront.Counter);
        Assert.Equal(save.Rng.Rngs["shuffle"], rs.Rng.Shuffle.ToSnapshot());
        Assert.Equal(save.Rng.Rngs["treasure_room_relics"], rs.Rng.TreasureRoomRelics.ToSnapshot());
        Assert.Equal("G1YFC196B2H2", rs.Rng.StringSeed);
        var prng = Assert.NotNull(player.PlayerRng);
        Assert.Equal(7378453647942262644uL, prng.Seed);
        Assert.Equal(saved.Rng.Rngs["rewards"], prng.Rewards.ToSnapshot());
        Assert.Equal(saved.Rng.Rngs["shops"], prng.Shops.ToSnapshot());
        Assert.Equal(163, rs.Unlocks.NumberOfRuns);
        Assert.True(rs.Unlocks.HasSeenEncounter("NIBBITS_WEAK"));
        Assert.True(rs.Unlocks.IsEpochRevealed("SILENT6_EPOCH"));
        Assert.Equal(17, rs.VisitedMapCoords.Count);
        Assert.Equal(17, rs.TotalFloor);
        Assert.Equal(MapPointType.Boss, rs.MapPointHistory[16].MapPointType);
        Assert.Equal(3, rs.VisitedEventIds.Count);
        Assert.True(rs.VisitedEventIds.Contains("SELF_HELP_BOOK"));
        Assert.True(rs.ExtraFields.StartedWithNeow);
        Assert.Equal(0, rs.CurrentActIndex);
        var config = Assert.NotNull(rs.Config);
        Assert.Equal(("G1YFC196B2H2", "SILENT", 8, "UNDERDOCKS"), (config.SeedText, config.CharacterId, config.Ascension, config.Act1));
        Assert.Equal(saved.Deck.Count, rs.NextCardInstanceId, "deck cards take instance ids 0..n-1 in save order");
    }

    public static void Test_SaveImport_CardLoadOrderEnchantThenUpgrade()
    {
        if (!CardRegistry.TryGet("TAUNT", out var def) || def is UnportedCardDef || !EnchantmentRegistry.Models.Contains("NIMBLE"))
        {
            return;
        }
        var save = PortedOnlyFixture();
        var rs = RunImporter.ImportCore(save, ImportOptions.Default);
        var taunt = rs.Players[0].Deck.Single(c => c.Id == "TAUNT");
        Assert.Equal(1, taunt.UpgradeLevel);
        Assert.Equal("NIMBLE", taunt.Enchantment!.Id);
        Assert.Equal(2, taunt.Enchantment.Amount);
        Assert.True(ReferenceEquals(rs.Players[0], taunt.Owner), "deck cards are attached to the imported player");
    }

    private const string GremlinMercEncounter = "ENCOUNTER.GREMLIN_MERC_NORMAL";

    private static SavedRun PortedOnlyFixtureWithoutGremlinMerc() => Edit(root =>
    {
        KeepPortedOnly(root);
        string replacement = "ENCOUNTER." + EncounterRegistry.Defs.AllIds.First(id => id.EndsWith("_NORMAL", StringComparison.Ordinal));
        foreach (var act in root["acts"]!.AsArray())
        {
            var normals = act!["rooms"]!["normal_encounter_ids"]!.AsArray();
            for (int i = 0; i < normals.Count; i++)
            {
                if ((string)normals[i]! == GremlinMercEncounter)
                {
                    normals[i] = replacement;
                }
            }
        }
    });

    public static void Test_SaveImport_FullStateThroughRunLayerOwners()
    {
        var save = PortedOnlyFixtureWithoutGremlinMerc();
        var reasons = RunImporter.CheckSupported(save, ImportOptions.Default);
        Assert.Equal(0, reasons.Count, string.Join("; ", reasons));
        var imported = RunImporter.ImportState(save, ImportOptions.Default);
        var rs = imported.State;
        Assert.Equal(SavePoint.PostCombatRewards, imported.EntryPoint);
        Assert.Equal(3, rs.Acts.Count);
        Assert.Equal(15, rs.Acts[0].Rooms.normalEncounters.Count);
        Assert.Equal(4, rs.Acts[0].Rooms.normalEncountersVisited);
        Assert.Equal(-0.05f, rs.Players[0].PlayerOdds!.CardRarity.CurrentValue);
        Assert.Equal(0.20000002f, rs.Players[0].PlayerOdds!.PotionReward.CurrentValue);
        Assert.False(rs.SharedRelicGrabBag!.RefreshAllowed);
        Assert.Equal(save.Odds.UnknownMapPointMonsterOddsValue, rs.Odds!.UnknownMapPoint.MonsterOdds);
        Assert.Equal(save.Odds.UnknownMapPointShopOddsValue, rs.Odds!.UnknownMapPoint.ShopOdds);
        foreach (var (rarity, ids) in save.SharedRelicGrabBag.RelicIdLists)
        {
            Assert.SequenceEqual(ids.Where(RelicCatalog.Contains), rs.SharedRelicGrabBag.GetDequeContents(rarity), "shared bag " + rarity);
        }
        var map = Assert.NotNull(rs.Map);
        Assert.True(map is SavedActMap);
        var savedMap = save.Acts[save.CurrentActIndex].SavedMap!;
        Assert.Equal(savedMap.Boss!.Coord, map.BossMapPoint.coord);
        Assert.Equal(savedMap.Start!.Coord, map.StartingMapPoint.coord);
        foreach (var point in savedMap.Points)
        {
            var loaded = Assert.NotNull(map.GetPoint(point.Coord));
            Assert.Equal(point.Type, loaded.PointType, point.Coord.ToString());
            Assert.Equal(point.Children.Count, loaded.Children.Count, point.Coord.ToString());
        }

        var sim = RunSim.FromSave(save, ImportOptions.Default);
        Assert.Equal(RunPhase.Rewards, sim.Phase, "a boss saved at point B resumes on its rewards screen");
        var room = Assert.NotNull(RoomStack.CurrentRoom(sim.State) as CombatRoom);
        Assert.True(room.IsPreFinished);
        Assert.Equal(RoomType.Boss, room.RoomType);
        Assert.Equal(1, sim.State.CurrentRooms.Count);
        Assert.Equal(17, sim.State.ActFloor, "the boss floor of act 1");
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        Assert.True(sim.LegalActions().Count > 0);

        W1Pending.Run(nameof(Test_SaveImport_FullStateThroughRunLayerOwners) + ".GremlinMerc", "content-leftovers", () =>
        {
            var full = RunImporter.CheckSupported(PortedOnlyFixture(), ImportOptions.Default);
            if (full.Count > 0)
            {
                throw new NotImplementedException("content-leftovers: " + full[0]);
            }
            RunImporter.ImportState(PortedOnlyFixture(), ImportOptions.Default);
        });
    }
}
