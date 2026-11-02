using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

// W4.1 combat-fixes (sim/specs/diff-report-w4-combat.md W4.1): regression tests from minimal repros (seed, start
// state, action prefix) for the kernel fixes and the widened combat gate.
public static class W41CombatFixesTests
{
    private static CombatSim Combat(string seed, string encounter, int ascension = 0)
        => CombatSim.Create(new RunRngSet(seed), CardSpec.FromIds(StartingDecks.Ironclad), encounter, ascension, 70, 80);

    private static MonsterState Monster(CombatSim sim, string id) => sim.State.Monsters.First(m => m.Def.Id == id);

    private static PowerModel? Power(CreatureState c, string id) => c.Powers.FirstOrDefault(p => p.Id == id);

    // K4FB4086NTRE s21 (TERROR_EEL_ELITE, A7): the eel's Thrash Vigor was consumed by its next attack in the game
    // (recorded power_received VIGOR -6 on the enemy side, round 3) but stayed in the sim. Repro: seed K4FB4086NTRE,
    // TERROR_EEL_ELITE, Vigor 6 on the eel, end turn; its turn-1 CRASH must deal +6 and spend the Vigor
    // (Models.Powers/VigorPower.cs BeforeAttack; Commands.Builders/AttackCommand.cs:781-791 FromMonster).
    public static void Test_W41_MonsterAttackConsumesVigor()
    {
        var baseline = Combat("K4FB4086NTRE", "TERROR_EEL_ELITE", 7);
        var vigor = baseline.Clone();
        var eel = Monster(vigor, "TERROR_EEL");
        PowerCmd.Apply(vigor.State, VigorPower.PowerId, eel, 6m, eel, null);
        Assert.Equal(6, Power(eel, VigorPower.PowerId)!.Amount);
        baseline.Step(GameAction.EndTurn);
        vigor.Step(GameAction.EndTurn);
        int dealtBase = 70 - baseline.State.Player.CurrentHp;
        int dealtVigor = 70 - vigor.State.Player.CurrentHp;
        Assert.True(dealtBase > 0, "the eel attacks on turn 1 (CRASH)");
        Assert.Equal(dealtBase + 6, dealtVigor, "Vigor adds to the monster attack");
        Assert.Null(Power(Monster(vigor, "TERROR_EEL"), VigorPower.PowerId), "the monster attack consumes the Vigor");
    }

    // DSB363J2LQWZ f5 (GREMLIN_MERC_NORMAL) / EC039PTFQWV5 f11 (LIVING_FOG_NORMAL): the floor summary lists every enemy
    // added mid-combat (Commands/CreatureCmd.cs:302-307). Repro: seed EC039PTFQWV5, LIVING_FOG_NORMAL with a map point
    // history entry, end turns until LIVING_FOG spawns a GAS_BOMB.
    public static void Test_W41_SpawnedEnemiesAppendToTheRoomHistory()
    {
        var sim = Combat("EC039PTFQWV5", "LIVING_FOG_NORMAL");
        var entry = new MapPointHistoryEntry(MapPointType.Monster);
        entry.Rooms.Add(new MapPointRoomHistoryEntry(RoomType.Monster, "LIVING_FOG_NORMAL", new List<string> { "LIVING_FOG" }, 0));
        sim.State.Run.MapPointHistory.Add(entry);
        var before = sim.Clone();
        for (int turn = 0; turn < 6 && !sim.IsTerminal && !sim.State.Monsters.Any(m => m.Def.Id == "GAS_BOMB"); turn++)
        {
            sim.Step(GameAction.EndTurn);
        }
        Assert.True(sim.State.Monsters.Any(m => m.Def.Id == "GAS_BOMB"), "LIVING_FOG spawned a GAS_BOMB");
        var ids = sim.State.Run.MapPointHistory[^1].Rooms[^1].MonsterIds;
        Assert.SequenceEqual(new[] { "LIVING_FOG", "GAS_BOMB" }, ids.ToArray());
        Assert.SequenceEqual(new[] { "LIVING_FOG" }, before.State.Run.MapPointHistory[^1].Rooms[^1].MonsterIds.ToArray());
    }

    private static DiffBundle Bundle(string character, IReadOnlyList<string> deck, IReadOnlyList<string> relics, string encounter = "NIBBITS_WEAK", string seed = "G0G2JMFEYHRS")
        => new()
        {
            Source = new BundleSource("W41", "p", 1, 1, 2),
            GameVersion = "v0.111.0",
            Seeding = RngSetup.SeedingXxh64,
            Seed = seed,
            Character = character,
            EncounterId = encounter,
            PlayerHp = 60,
            PlayerMaxHp = 75,
            Deck = deck.Select(id => new BundleCard(id, false)).ToList(),
            Relics = relics,
            Outcome = DiffBundle.OutcomeAbandoned
        };

    // §5 rows "23 Regent v0.107.1 first combats" and "B PLRUV49CRFQW s90": the gate builds the bundle's character
    // with its relics inside a CombatRoom, firing AfterRoomEntered after SetUpCombat (Rooms/CombatRoom.cs StartCombat),
    // so DIVINE_RIGHT's 3 stars (Models.Relics/DivineRight.cs:53,90) are there at decision 0.
    public static void Test_W41_GateBuildsCharacterRelicsAndRoom()
    {
        var regent = CharacterRegistry.Get(Regent.CharacterId);
        var sim = BundleRunner.CreateSim(Bundle(regent.Id, regent.StartingDeck, regent.StartingRelics));
        Assert.Equal(regent.Id, sim.State.Player.CharacterId);
        Assert.Equal(3, sim.State.Player.Stars, "DIVINE_RIGHT gains its stars on room entry");
        Assert.True(RoomStack.CurrentRoom(sim.State.Run) is CombatRoom, "the combat runs inside a CombatRoom");
        Assert.Equal(60, sim.State.Player.CurrentHp);
        Assert.Null(BundleRunner.IneligibleReason(Bundle(regent.Id, regent.StartingDeck, regent.StartingRelics)));
        Assert.True(BundleRunner.IneligibleReason(Bundle("NOT_A_CHARACTER", regent.StartingDeck, regent.StartingRelics))!.Contains("character NOT_A_CHARACTER", StringComparison.Ordinal));

        var extras = new CombatStartExtras(new[] { new KeyValuePair<int, string>(1, "FIRE_POTION") }, 2, 99);
        var silent = BundleRunner.CreateSim(Bundle("SILENT", StartingDecks.Ironclad, new[] { "RING_OF_THE_SNAKE" }), extras);
        Assert.Equal(2, silent.State.Player.MaxPotionCount, "max potion slots are set before the potions");
        Assert.Equal("FIRE_POTION", silent.State.Player.PotionSlots[1]!.Id);
        Assert.Equal(99, silent.State.Player.Gold);
    }

    // §5 row "G0 ... final": BURNING_BLOOD heals 6 in AfterCombatVictory (Models.Relics/BurningBlood.cs:52) after the
    // recorder's last in-combat snapshot; the harness compares the HP at combat end. Repro: seed G0G2JMFEYHRS,
    // NIBBITS_WEAK, Ironclad starter deck + BURNING_BLOOD at 60/75, play the first playable card on the first enemy.
    public static void Test_W41_FinalHpIsReadBeforeVictoryHeals()
    {
        var sim = BundleRunner.CreateSim(Bundle(StartingDecks.IroncladCharacterId, StartingDecks.Ironclad, new[] { "BURNING_BLOOD" }));
        for (int i = 0; i < 400 && !sim.IsTerminal; i++)
        {
            var legal = sim.LegalActions();
            var play = legal.Where(a => a.Kind == ActionKind.PlayCard).Cast<GameAction?>().FirstOrDefault();
            sim.Step(play ?? GameAction.EndTurn);
        }
        Assert.Equal(CombatOutcome.Victory, sim.Outcome);
        int atEnd = sim.State.PlayerHpAtCombatEnd!.Value;
        Assert.Equal(Math.Min(atEnd + 6, sim.State.Player.MaxHp), sim.State.Player.CurrentHp, "BURNING_BLOOD heals after the end-of-combat HP");
        Assert.Equal(atEnd, BundleRunner.FinalHp(sim.State));
        Assert.Equal(atEnd, sim.Clone().State.PlayerHpAtCombatEnd);
    }

    // §5 rows "7 Silent v0.111.0 first combats" and "B PLRUV49CRFQW s65/s90/s94": hand-select candidates are the hand
    // in hand order (Commands/CardSelectCmd.cs:3687) while the recorder lists UI order; mode / can_confirm / index are
    // unprojected UI fields.
    public static void Test_W41_HandSelectComparesAsMultisetAndPicksByIdentity()
    {
        JsonObject Cand(int i, string id) => new() { ["index"] = i, ["id"] = id, ["upgraded"] = false };
        var oracle = new JsonObject
        {
            ["kind"] = "hand_select",
            ["mode"] = "simple_select",
            ["candidates"] = new JsonArray(Cand(0, "STRIKE_SILENT"), Cand(1, "DEFEND_SILENT"), Cand(2, "DEFEND_SILENT")),
            ["can_confirm"] = true
        };
        var sim = new JsonObject
        {
            ["kind"] = "hand_select",
            ["candidates"] = new JsonArray(
                new JsonObject { ["id"] = "DEFEND_SILENT", ["upgraded"] = false },
                new JsonObject { ["id"] = "STRIKE_SILENT", ["upgraded"] = false },
                new JsonObject { ["id"] = "DEFEND_SILENT", ["upgraded"] = false })
        };
        var diffs = V2Runner.CompareCombatSelect(oracle, sim);
        Assert.Equal(0, diffs.Count(d => d.Severity == DiffSeverity.Hard), string.Join("; ", diffs.Select(d => d.Path)));
        Assert.True(diffs.Any(d => d.Path == "select.mode" && d.Severity == DiffSeverity.Soft));
        var other = (JsonObject)oracle.DeepClone();
        other["candidates"]![0]!["id"] = "NEUTRALIZE";
        Assert.True(V2Runner.CompareCombatSelect(other, sim).Any(d => d.Severity == DiffSeverity.Hard), "a different multiset still diverges");
        var choose = new JsonObject
        {
            ["type"] = "choose",
            ["kind"] = "hand",
            ["picks"] = new JsonArray(new JsonObject { ["index"] = 2, ["card_id"] = "DEFEND_SILENT", ["upgraded"] = false })
        };
        var action = new V2Action(V2Action.Choose, choose, new[] { new ChoosePick(2, null, null, "DEFEND_SILENT", false) });
        Assert.Null(ActionMapper.HandPicksByIdentity(action)[0].Index);
    }

    // GM28D28DX794 f14 (SKULKING_COLONY_ELITE), PLRUV49CRFQW f11 (BYGONE_EFFIGY_ELITE): the projection shows the game's
    // DisplayAmount (Models.Powers/HardenedShellPower.cs:25, SlowPower.cs:24, TenderPower.cs:229). Repro: seed
    // GM28D28DX794 / PLRUV49CRFQW, the encounter at decision 0, then one Strike on the shell owner.
    public static void Test_W41_DisplayAmountRulesForShellSlowTender()
    {
        var colony = Combat("GM28D28DX794", "SKULKING_COLONY_ELITE", 6);
        var owner = colony.State.Monsters.First(m => Power(m, HardenedShellPower.PowerId) != null);
        var shell = (HardenedShellPower)Power(owner, HardenedShellPower.PowerId)!;
        Assert.Equal(shell.Amount, SimProjector.DisplayAmount(shell));
        int strike = colony.State.Player.Hand.Cards.ToList().FindIndex(c => c.Id == "STRIKE_IRONCLAD");
        Assert.True(strike >= 0, "a Strike is in the opening hand");
        colony.Step(GameAction.Play(strike, colony.State.Monsters.IndexOf(owner)));
        shell = (HardenedShellPower)Power(owner, HardenedShellPower.PowerId)!;
        Assert.True(shell.DamageReceivedThisTurn > 0m, "the Strike landed");
        Assert.Equal((int)Math.Max(0m, shell.Amount - shell.DamageReceivedThisTurn), SimProjector.DisplayAmount(shell));
        SimProjector.Project(colony.State);

        var effigy = Combat("PLRUV49CRFQW", "BYGONE_EFFIGY_ELITE", 8);
        var slowOwner = effigy.State.Monsters.First(m => Power(m, SlowPower.PowerId) != null);
        int play = effigy.State.Player.Hand.Cards.ToList().FindIndex(c => c.Id == "DEFEND_IRONCLAD");
        effigy.Step(GameAction.Play(play, GameAction.NoIndex));
        var slow = (SlowPower)Power(slowOwner, SlowPower.PowerId)!;
        Assert.Equal(1, slow.SlowAmount);
        Assert.Equal(10, SimProjector.DisplayAmount(slow));
        SimProjector.Project(effigy.State);

        var tender = new TenderPower();
        Assert.Equal(0, SimProjector.DisplayAmount(tender));

        // PLRUV49CRFQW s29 decision 8 / GM28D28DX794 s32 decision 12: rolled-forward pre_states (oracle.rolled_forward,
        // pending card_play_finished / damage_received) cannot advance these counters, so the amount is soft there.
        var slowDiff = new FieldDiff("enemies[combat_id=1].powers[SLOW_POWER].amount", 30, 40, DiffSeverity.Hard);
        Assert.Equal(DiffSeverity.Soft, V2Runner.SoftenRolledForwardCounter(slowDiff).Severity);
        var strength = new FieldDiff("enemies[combat_id=1].powers[STRENGTH_POWER].amount", 1, 2, DiffSeverity.Hard);
        Assert.Equal(DiffSeverity.Hard, V2Runner.SoftenRolledForwardCounter(strength).Severity);
    }

    // mcr hand indices by simulation (Q3X6PF8S37GA-part4 decision 1: DEFEND recorded at hand index 0, the next checksum
    // context says index 2). Repro: seeds W41MCR0000.., NIBBITS_WEAK, Ironclad starter deck; the recorded index names the
    // first copy of a duplicated card, the next checksum hand is the one left by playing a later copy.
    public static void Test_W41_McrPlayIndexIsResolvedBySimulation()
    {
        // The first seed whose opening hand holds two copies of a card with another card between them (so the copies
        // leave different hand orders).
        CombatSim sim = null!;
        List<CardInstance> hand = null!;
        string? dup = null;
        for (int s = 0; s < 64 && dup == null; s++)
        {
            sim = BundleRunner.CreateSim(Bundle(StartingDecks.IroncladCharacterId, StartingDecks.Ironclad, Array.Empty<string>(), seed: "W41MCR" + s.ToString("D4")));
            hand = sim.State.Player.Hand.Cards.ToList();
            dup = hand.GroupBy(c => c.Id).Select(g => g.Key).FirstOrDefault(id =>
            {
                int a = hand.FindIndex(c => c.Id == id), b = hand.FindLastIndex(c => c.Id == id);
                return b > a + 1 && hand.Skip(a + 1).Take(b - a - 1).Any(c => c.Id != id);
            });
        }
        Assert.NotNull(dup);
        bool targeted = hand.First(c => c.Id == dup).TargetType == TargetType.AnyEnemy;
        int targetIndex = targeted ? 0 : GameAction.NoIndex;
        JsonNode? targetId = targeted ? JsonValue.Create(sim.State.Monsters[0].CombatId) : null;
        int first = hand.FindIndex(c => c.Id == dup);
        int later = hand.FindLastIndex(c => c.Id == dup);
        var driver = new StandaloneCombatDriver(sim);
        var truth = driver.Clone();
        truth.Step(GameAction.Play(later, targetIndex));
        var nextHand = new JsonArray(truth.State!.Player.Hand.Cards.Select(c => (JsonNode)JsonValue.Create(c.IsUpgraded ? c.Id + "+" : c.Id)!).ToArray());
        var recorded = new V2Action(V2Action.PlayCard, new JsonObject { ["type"] = "play_card", ["hand_index"] = first, ["card_id"] = dup, ["upgraded"] = false, ["target_combat_id"] = targetId?.DeepClone() });
        var next = new SegmentDecision
        {
            Index = 1,
            Action = new V2Action(V2Action.EndTurn, new JsonObject { ["type"] = "end_turn" }),
            Oracle = new JsonObject { ["source"] = "mcr", ["checksum_state"] = new JsonObject { ["hand"] = nextHand } }
        };
        var resolved = V2Runner.ResolveMcrPlayIndex(driver, recorded, next);
        Assert.Equal(later, resolved!.Int("hand_index"));
        var noChecksum = new SegmentDecision { Index = 1, Action = next.Action };
        Assert.Null(V2Runner.ResolveMcrPlayIndex(driver, recorded, noChecksum), "without a next hand the recorded copy stands");
        var wrongIndex = new V2Action(V2Action.PlayCard, new JsonObject { ["type"] = "play_card", ["hand_index"] = 99, ["card_id"] = dup, ["upgraded"] = false, ["target_combat_id"] = targetId?.DeepClone() });
        Assert.Equal(first, V2Runner.ResolveMcrPlayIndex(driver, wrongIndex, noChecksum)!.Int("hand_index"));
    }

    // Exporter reasons closed by the widened gate: relic_not_whitelisted (the gate builds ported relics) and
    // card_upgrade_not_ported for registered cards (HIDDEN_DAGGERS upgrades through an IsUpgraded branch,
    // Models.Cards/HiddenDaggers.cs:122).
    public static void Test_W41_GateClosesStaleExporterReasons()
    {
        var open = BundleRunner.OpenExporterReasons(new[] { "relic_not_whitelisted:RING_OF_THE_SNAKE", "card_upgrade_not_ported:HIDDEN_DAGGERS", "card_upgrade_not_ported:NOT_A_CARD", "pre_state_hand_unsettled:x" });
        Assert.SequenceEqual(new[] { "card_upgrade_not_ported:NOT_A_CARD", "pre_state_hand_unsettled:x" }, open.ToArray());
    }

    // Experiment B: a later combat segment with an mcr_initial anchor gets the decoded replay's rng_offsets, deck and
    // relics when its start matches; the anchor and deck-order gaps are closed, a mismatch is an anchor_error.
    public static void Test_W41_McrJoinTakesTheDecodedStart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sts2sim-w41-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "mcr", "ineligible"));
        try
        {
            var deck = new JsonArray(StartingDecks.Ironclad.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["upgraded"] = false }).ToArray());
            var multiset = new JsonArray(StartingDecks.Ironclad.OrderBy(x => x, StringComparer.Ordinal).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
            var decoded = new JsonObject
            {
                ["source"] = new JsonObject { ["floor"] = 6 },
                ["encounter_id"] = "NIBBITS_WEAK",
                ["player"] = new JsonObject { ["hp"] = 60, ["max_hp"] = 80 },
                ["deck"] = deck,
                ["relics"] = new JsonArray("BURNING_BLOOD"),
                ["rng_offsets"] = new JsonObject { ["Shuffle"] = 3 },
                ["ineligible_reasons"] = new JsonArray("unsupported_action:player_choice")
            };
            File.WriteAllText(Path.Combine(dir, "mcr", "ineligible", "W41-part2.json"), decoded.ToJsonString());
            JsonObject Segment(int hp) => new()
            {
                ["id"] = "s9",
                ["kind"] = "combat",
                ["act"] = 1,
                ["floor"] = 6,
                ["room_type"] = "monster",
                ["room_model_id"] = "ENCOUNTER.NIBBITS_WEAK",
                ["start_anchor"] = "a1",
                ["start"] = new JsonObject
                {
                    ["encounter_id"] = "NIBBITS_WEAK",
                    ["player"] = new JsonObject { ["hp"] = hp, ["max_hp"] = 80 },
                    ["deck"] = null,
                    ["deck_multiset"] = multiset.DeepClone(),
                    ["relics"] = new JsonArray("BURNING_BLOOD"),
                    ["anchor"] = "a1"
                },
                ["decisions"] = new JsonArray(),
                ["outcome"] = new JsonObject { ["result"] = "win" },
                ["eligibility"] = new JsonObject { ["eligible"] = false, ["reasons"] = new JsonArray("anchor_unreadable:mcr_decoder_missing", "recorder_gap:deck_order") }
            };
            JsonObject Run(int hp) => new()
            {
                ["bundle_version"] = 2,
                ["kind"] = "run",
                ["source"] = new JsonObject { ["lineage"] = "W41" },
                ["game_version"] = "v0.111.0",
                ["rules_version"] = "v0.111.0",
                ["seeding"] = "xxh64",
                ["seed"] = "G0G2JMFEYHRS",
                ["character"] = StartingDecks.IroncladCharacterId,
                ["ascension"] = 0,
                ["game_mode"] = "standard",
                ["modifiers"] = new JsonArray(),
                ["anchors"] = new JsonArray(new JsonObject { ["id"] = "a1", ["kind"] = "mcr_initial", ["act"] = 1, ["floor"] = 6, ["ref"] = "W41-part2/native/replay.mcr" }),
                ["segments"] = new JsonArray(Segment(hp)),
                ["eligibility"] = new JsonObject { ["run_reasons"] = new JsonArray() }
            };
            string path = Path.Combine(dir, "W41.json");
            File.WriteAllText(path, Run(60).ToJsonString());
            var joined = RunBundleJson.Parse(File.ReadAllText(path), path).Segments[0];
            Assert.Equal("W41-part2", joined.McrJoinedPart);
            // W4.1 integrate: the join supplies the start.deck the reader marked unavailable, so the runner does not skip it.
            Assert.Equal(0, joined.UnavailableFields.Count, string.Join(", ", joined.UnavailableFields));
            Assert.NotNull(joined.CombatStart);
            Assert.Equal(0, joined.CombatStart!.IneligibleReasons.Count, string.Join("; ", joined.CombatStart.IneligibleReasons));
            Assert.Equal(3, joined.CombatStart.RngOffsets.Single(kv => kv.Key == "Shuffle").Value);
            Assert.Equal(StartingDecks.Ironclad.Count, joined.CombatStart.Deck.Count);

            File.WriteAllText(path, Run(59).ToJsonString());
            var mismatched = RunBundleJson.Parse(File.ReadAllText(path), path).Segments[0];
            Assert.Null(mismatched.McrJoinedPart);
            Assert.True(mismatched.McrJoinNote!.StartsWith("mcr_join_mismatch: hp", StringComparison.Ordinal), mismatched.McrJoinNote);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
