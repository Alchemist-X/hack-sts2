using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

public static class DiffTests
{
    private const string EncounterId = "NIBBITS_WEAK";
    private const string TestSeed = "G0G2JMFEYHRS";
    private const string LegacySeed = "3URCWVGADS";
    private const string Version = "v0.111.0";
    private const int MaxRecordedDecisions = 400;

    private static List<BundleCard> StarterBundleDeck(int ascension)
        => StartingDecks.IroncladAtRunStart(ascension).Select(id => new BundleCard(id, false)).ToList();

    private static CombatSim OpeningSim(int ascension = 10, string seed = TestSeed)
    {
        var rng = RngSetup.Create(RngSetup.SeedingXxh64, seed);
        var deck = StarterBundleDeck(ascension).Select(c => new CardSpec(c.Id, c.Upgraded)).ToArray();
        return CombatSim.Create(rng, deck, EncounterId, ascension, StartingDecks.IroncladHpAfterNeowArrival(ascension), StartingDecks.IroncladStartingHp);
    }

    private static Projection Mutated(Projection p, Action<JsonObject> mutate)
    {
        var json = ProjectionJson.Write(p);
        mutate(json);
        return ProjectionJson.Read(json);
    }

    private static void Bump(JsonNode? obj, string key, int by = 1)
    {
        var o = obj!;
        o[key] = o[key]!.GetValue<int>() + by;
    }

    private static GameAction PickAction(CombatSim sim, List<GameAction> buffer, Rng? policy)
    {
        sim.LegalActions(buffer);
        if (policy != null)
        {
            return buffer[policy.NextInt(buffer.Count)];
        }
        foreach (var a in buffer)
        {
            if (a.Kind == ActionKind.PlayCard)
            {
                return a;
            }
        }
        return GameAction.EndTurn;
    }

    private static DiffBundle RecordBundle(string seeding, string seed, IReadOnlyList<KeyValuePair<string, int>> offsets, int ascension,
        ulong? policySeed, string gameVersion = Version, int maxDecisions = MaxRecordedDecisions)
    {
        var deck = StarterBundleDeck(ascension);
        int hp = StartingDecks.IroncladHpAfterNeowArrival(ascension);
        var rng = RngSetup.Create(seeding, seed);
        RngSetup.ApplyOffsets(rng, offsets);
        var sim = CombatSim.Create(rng, deck.Select(c => new CardSpec(c.Id, c.Upgraded)).ToArray(), EncounterId, ascension, hp, StartingDecks.IroncladStartingHp);
        var policy = policySeed is ulong ps ? new Rng(ps) : null;
        var buffer = new List<GameAction>();
        var decisions = new List<BundleDecision>();
        while (!sim.IsTerminal && decisions.Count < maxDecisions)
        {
            var projection = SimProjector.Project(sim.State);
            var action = PickAction(sim, buffer, policy);
            BundleAction recorded;
            if (action.Kind == ActionKind.EndTurn)
            {
                recorded = BundleAction.EndTurn;
            }
            else
            {
                var card = sim.State.Player.Hand[action.CardIndex];
                int? target = action.TargetIndex >= 0 ? sim.State.Monsters[action.TargetIndex].CombatId : null;
                recorded = BundleAction.Play(action.CardIndex, card.Id, card.IsUpgraded, target);
            }
            decisions.Add(new BundleDecision
            {
                Index = decisions.Count,
                PreState = projection,
                PreStateJson = ProjectionJson.Write(projection),
                Action = recorded
            });
            sim.Step(action);
        }
        return new DiffBundle
        {
            Source = new BundleSource("TEST", "test-part", 1, 1, 2),
            GameVersion = gameVersion,
            Seeding = seeding,
            Seed = seed,
            Character = StartingDecks.IroncladCharacterId,
            Ascension = ascension,
            EncounterId = EncounterId,
            PlayerHp = hp,
            PlayerMaxHp = StartingDecks.IroncladStartingHp,
            Deck = deck,
            Relics = new[] { "BURNING_BLOOD", "NEOWS_TALISMAN" },
            RngOffsets = offsets,
            Decisions = decisions,
            Outcome = BundleRunner.OutcomeName(sim.Outcome),
            FinalHp = sim.State.Player.CurrentHp,
            FilePath = $"memory://{seeding}/{seed}/{policySeed?.ToString() ?? "greedy"}"
        };
    }

    private static JsonObject BundleNode(DiffBundle b) => JsonNode.Parse(DiffReport.Serialize(BundleJson.Write(b)))!.AsObject();

    private static DiffBundle Reparse(JsonObject node, string? path = null) => BundleJson.Parse(node.ToJsonString(), path);

    private static readonly KeyValuePair<string, int>[] NoOffsets = Array.Empty<KeyValuePair<string, int>>();

    public static void Test_Diff_ComparatorIdenticalProjectionsHaveNoDiffs()
    {
        var p = SimProjector.Project(OpeningSim().State);
        Assert.Equal(0, ProjectionComparer.Compare(p, p).Count);
        Assert.Equal(0, ProjectionComparer.Compare(ProjectionJson.Read(ProjectionJson.Write(p)), p).Count, "a JSON round trip changes nothing");
        Assert.Null(ProjectionComparer.FirstHard(ProjectionComparer.Compare(p, p)));
    }

    public static void Test_Diff_ComparatorFieldClasses()
    {
        var baseline = SimProjector.Project(OpeningSim().State);
        const DiffSeverity H = DiffSeverity.Hard;
        const DiffSeverity S = DiffSeverity.Soft;
        var cases = new (string Name, Action<JsonObject> Mutate, string Path, DiffSeverity Severity)[]
        {
            ("player_turn", j => Bump(j["turn"], "player_turn"), "turn.player_turn", H),
            ("phase", j => j["turn"]!["phase"] = "enemy", "turn.phase", H),
            ("hp", j => Bump(j["player"], "hp", -1), "player.hp", H),
            ("max_hp", j => Bump(j["player"], "max_hp"), "player.max_hp", H),
            ("block", j => Bump(j["player"], "block"), "player.block", H),
            ("energy", j => Bump(j["player"], "energy", -1), "player.energy", H),
            ("max_energy", j => Bump(j["player"], "max_energy"), "player.max_energy", H),
            ("hand id", j => j["player"]!["hand"]![0]!["id"] = "NOT_A_CARD", "player.hand[0].id", H),
            ("hand upgraded", j => j["player"]!["hand"]![1]!["upgraded"] = !j["player"]!["hand"]![1]!["upgraded"]!.GetValue<bool>(), "player.hand[1].upgraded", H),
            ("hand cost", j => j["player"]!["hand"]![2]!["cost"] = "X", "player.hand[2].cost", H),
            ("hand can_play", j => j["player"]!["hand"]![3]!["can_play"] = !j["player"]!["hand"]![3]!["can_play"]!.GetValue<bool>(), "player.hand[3].can_play", H),
            ("hand length", j => j["player"]!["hand"]!.AsArray().RemoveAt(4), "player.hand.length", H),
            ("draw_count", j => Bump(j["player"], "draw_count"), "player.draw_count", H),
            ("discard_count", j => Bump(j["player"], "discard_count"), "player.discard_count", H),
            ("exhaust_count", j => Bump(j["player"], "exhaust_count"), "player.exhaust_count", H),
            ("player power", j => j["player"]!["powers"]!.AsArray().Add(new JsonObject { ["id"] = "WEAK_POWER", ["amount"] = 1 }), "player.powers[WEAK_POWER]", H),
            ("enemy hp", j => Bump(j["enemies"]![0], "hp", -1), "enemies[combat_id=1].hp", H),
            ("enemy max_hp", j => Bump(j["enemies"]![0], "max_hp"), "enemies[combat_id=1].max_hp", H),
            ("enemy block", j => Bump(j["enemies"]![0], "block"), "enemies[combat_id=1].block", H),
            ("enemy id", j => j["enemies"]![0]!["monster_id"] = "NOT_A_MONSTER", "enemies[combat_id=1].monster_id", H),
            ("enemy combat id", j => j["enemies"]![0]!["combat_id"] = 7, "enemies.combat_ids", H),
            ("enemy power", j => j["enemies"]![0]!["powers"]!.AsArray().Add(new JsonObject { ["id"] = "STRENGTH_POWER", ["amount"] = 2 }), "enemies[combat_id=1].powers[STRENGTH_POWER]", H),
            ("intent type", j => j["enemies"]![0]!["intents"]![0]!["type"] = "Buff", "enemies[combat_id=1].intents[0].type", H),
            ("intent value", j => Bump(j["enemies"]![0]!["intents"]![0], "attack_value"), "enemies[combat_id=1].intents[0].attack_value", H),
            ("intent times", j => Bump(j["enemies"]![0]!["intents"]![0], "attack_times"), "enemies[combat_id=1].intents[0].attack_times", H),
            ("intent count", j => j["enemies"]![0]!["intents"]!.AsArray().Add(new JsonObject { ["type"] = "Defend" }), "enemies[combat_id=1].intents.length", H),
            ("intent extra", j => j["enemies"]![0]!["intents"]![0]!["card_count"] = 2, "enemies[combat_id=1].intents[0].card_count", S),
            ("draw multiset", j => j["player"]!["draw_multiset"]![0] = "NOT_A_CARD", "player.draw_multiset", S),
            ("discard", j => j["player"]!["discard"]!.AsArray().Add("STRIKE_IRONCLAD"), "player.discard", S),
            ("exhaust", j => j["player"]!["exhaust"]!.AsArray().Add("DEFEND_IRONCLAD+"), "player.exhaust", S)
        };
        foreach (var c in cases)
        {
            var diffs = ProjectionComparer.Compare(Mutated(baseline, c.Mutate), baseline);
            Assert.Equal(1, diffs.Count, $"{c.Name}: [{string.Join("; ", diffs.Select(d => d.Path))}]");
            Assert.Equal(c.Path, diffs[0].Path, c.Name);
            Assert.Equal(c.Severity, diffs[0].Severity, c.Name);
            Assert.Equal(c.Severity == H, ProjectionComparer.HasHard(diffs), c.Name);
        }
    }

    public static void Test_Diff_ComparatorPileAndOptionalFieldRules()
    {
        var baseline = SimProjector.Project(OpeningSim().State);
        Assert.True(baseline.Player.DrawMultiset!.Count >= 2);
        var reversed = Mutated(baseline, j =>
        {
            var arr = j["player"]!["draw_multiset"]!.AsArray();
            var items = arr.Select(n => n!.GetValue<string>()).Reverse().ToList();
            arr.Clear();
            foreach (var s in items)
            {
                arr.Add(s);
            }
        });
        Assert.Equal(0, ProjectionComparer.Compare(reversed, baseline).Count, "the draw pile is compared as a multiset");

        var withoutSoftLists = Mutated(baseline, j =>
        {
            var player = j["player"]!.AsObject();
            player.Remove("draw_multiset");
            player.Remove("discard");
            player.Remove("exhaust");
        });
        Assert.Null(withoutSoftLists.Player.DrawMultiset);
        Assert.Equal(0, ProjectionComparer.Compare(withoutSoftLists, baseline).Count, "absent soft lists are not compared");

        var discardA = new Projection
        {
            Turn = new TurnProjection(1, ProjectionJson.PhasePlayerPlay),
            Player = new PlayerProjection { Discard = new[] { "STRIKE_IRONCLAD", "DEFEND_IRONCLAD" } }
        };
        var discardB = new Projection
        {
            Turn = new TurnProjection(1, ProjectionJson.PhasePlayerPlay),
            Player = new PlayerProjection { Discard = new[] { "DEFEND_IRONCLAD", "STRIKE_IRONCLAD" } }
        };
        var orderDiffs = ProjectionComparer.Compare(discardA, discardB);
        Assert.Equal(1, orderDiffs.Count);
        Assert.Equal("player.discard", orderDiffs[0].Path);
        Assert.Equal(DiffSeverity.Soft, orderDiffs[0].Severity);
        Assert.Equal("same cards, different order", orderDiffs[0].Note);

        var noTimes = Mutated(baseline, j => j["enemies"]![0]!["intents"]![0]!.AsObject().Remove("attack_times"));
        Assert.Equal(1, noTimes.Enemies[0].Intents[0].AttackTimes, "a single-hit label carries no repeat count");
        Assert.Equal(0, ProjectionComparer.Compare(noTimes, baseline).Count);

        var noValue = Mutated(baseline, j =>
        {
            var intent = j["enemies"]![0]!["intents"]![0]!.AsObject();
            intent.Remove("attack_value");
            intent.Remove("attack_times");
        });
        var missing = ProjectionComparer.Compare(noValue, baseline);
        Assert.Equal(2, missing.Count);
        Assert.Equal("enemies[combat_id=1].intents[0].attack_value", missing[0].Path);
        Assert.Equal(DiffSeverity.Hard, missing[0].Severity);
        Assert.Equal(ProjectionComparer.NoteMissingInOracle, missing[0].Note);
    }

    public static void Test_Diff_ComparatorPowersMatchedById()
    {
        static Projection WithPowers(params PowerProjection[] powers) => new()
        {
            Turn = new TurnProjection(1, ProjectionJson.PhasePlayerPlay),
            Player = new PlayerProjection { Powers = powers }
        };
        var strength = new PowerProjection("STRENGTH_POWER", 2);
        var vulnerable = new PowerProjection("VULNERABLE_POWER", 1);

        var reordered = ProjectionComparer.Compare(WithPowers(strength, vulnerable), WithPowers(vulnerable, strength));
        Assert.Equal(1, reordered.Count);
        Assert.Equal("player.powers.order", reordered[0].Path);
        Assert.Equal(DiffSeverity.Soft, reordered[0].Severity);

        var amount = ProjectionComparer.Compare(WithPowers(strength, vulnerable), WithPowers(strength, vulnerable with { Amount = 2 }));
        Assert.Equal(1, amount.Count);
        Assert.Equal("player.powers[VULNERABLE_POWER].amount", amount[0].Path);
        Assert.Equal(DiffSeverity.Hard, amount[0].Severity);
        Assert.Equal(1, amount[0].Oracle!.GetValue<int>());
        Assert.Equal(2, amount[0].Sim!.GetValue<int>());

        var missing = ProjectionComparer.Compare(WithPowers(strength, vulnerable), WithPowers(strength));
        Assert.Equal(1, missing.Count);
        Assert.Equal("player.powers[VULNERABLE_POWER]", missing[0].Path);
        Assert.Equal(ProjectionComparer.NoteMissingInSim, missing[0].Note);

        var second = ProjectionComparer.Compare(WithPowers(strength, strength), WithPowers(strength));
        Assert.Equal(1, second.Count);
        Assert.Equal("player.powers[STRENGTH_POWER#2]", second[0].Path);
        Assert.Equal("player.powers[STRENGTH_POWER#2]", second[0].PathPattern);
        Assert.Equal("enemies[combat_id=*].intents[*].type", FieldDiff.PatternOf("enemies[combat_id=3].intents[1].type"));
    }

    public static void Test_Diff_ProjectionOfNibbitsWeakOpening()
    {
        const int ascension = 10;
        var sim = CombatSim.Create(RngSetup.Create(RngSetup.SeedingXxh64, TestSeed), StartingDecks.IroncladAtRunStart(ascension), EncounterId, ascension,
            StartingDecks.IroncladHpAfterNeowArrival(ascension), StartingDecks.IroncladStartingHp);
        Assert.Equal(sim.StateHash(), OpeningSim(ascension).StateHash(), "the card-spec Create the harness uses = the id-list Create for an unupgraded deck");
        var state = sim.State;
        var p = SimProjector.Project(state);

        Assert.Equal(new TurnProjection(1, ProjectionJson.PhasePlayerPlay), p.Turn);
        Assert.Equal(64, p.Player.Hp);
        Assert.Equal(80, p.Player.MaxHp);
        Assert.Equal(0, p.Player.Block);
        Assert.Equal(3, p.Player.Energy);
        Assert.Equal(3, p.Player.MaxEnergy);
        Assert.Equal(0, p.Player.Powers.Count);

        var expectedCost = new Dictionary<string, int>
        {
            ["STRIKE_IRONCLAD"] = 1,
            ["DEFEND_IRONCLAD"] = 1,
            ["BASH"] = 2,
            ["ASCENDERS_BANE"] = 0
        };
        Assert.Equal(5, p.Player.Hand.Count);
        for (int i = 0; i < p.Player.Hand.Count; i++)
        {
            var card = p.Player.Hand[i];
            Assert.Equal(state.Player.Hand[i].Id, card.Id, $"hand[{i}] keeps the sim hand order");
            Assert.False(card.Upgraded);
            Assert.Equal(CardCost.Of(expectedCost[card.Id]), card.Cost, $"hand[{i}] cost");
            Assert.Equal(card.Id != "ASCENDERS_BANE", card.CanPlay, $"hand[{i}] can_play");
        }

        Assert.Equal(6, p.Player.DrawCount);
        Assert.Equal(0, p.Player.DiscardCount);
        Assert.Equal(0, p.Player.ExhaustCount);
        Assert.SequenceEqual(Array.Empty<string>(), p.Player.Discard!);
        Assert.SequenceEqual(Array.Empty<string>(), p.Player.Exhaust!);
        Assert.SequenceEqual(p.Player.DrawMultiset!.OrderBy(s => s, StringComparer.Ordinal), p.Player.DrawMultiset!, "draw_multiset is sorted");
        var all = p.Player.Hand.Select(c => c.Id).Concat(p.Player.DrawMultiset!).OrderBy(s => s, StringComparer.Ordinal);
        Assert.SequenceEqual(StartingDecks.IroncladAtRunStart(ascension).OrderBy(s => s, StringComparer.Ordinal), all, "hand + draw = the deck");

        Assert.Equal(1, p.Enemies.Count);
        var nibbit = p.Enemies[0];
        Assert.Equal(1, nibbit.CombatId);
        Assert.Equal("NIBBIT", nibbit.MonsterId);
        Assert.True(nibbit.Hp >= 44 && nibbit.Hp <= 48, $"A8+ Nibbit HP roll in [44, 48], was {nibbit.Hp}");
        Assert.Equal(nibbit.Hp, nibbit.MaxHp);
        Assert.Equal(0, nibbit.Block);
        Assert.Equal(0, nibbit.Powers.Count);
        Assert.Equal(1, nibbit.Intents.Count);
        Assert.Equal("Attack", nibbit.Intents[0].Type);
        Assert.Equal(13, nibbit.Intents[0].AttackValue, "a lone Nibbit opens with the A9+ butt");
        Assert.Equal(1, nibbit.Intents[0].AttackTimes);
        Assert.Equal(0, nibbit.Intents[0].Extras.Count);

        var a0 = SimProjector.Project(OpeningSim(0).State);
        Assert.Equal(12, a0.Enemies[0].Intents[0].AttackValue, "A0 butt damage");
        Assert.Equal(10, a0.Player.DrawCount + a0.Player.Hand.Count, "A0 deck has no curse");
    }

    public static void Test_Diff_ProjectionIsPureAndTracksModifiers()
    {
        var sim = OpeningSim();
        ulong before = sim.StateHash();
        var first = DiffReport.Serialize(ProjectionJson.Write(SimProjector.Project(sim.State)));
        var second = DiffReport.Serialize(ProjectionJson.Write(SimProjector.Project(sim.State)));
        Assert.Equal(before, sim.StateHash(), "projecting does not change the state");
        Assert.Equal(first, second);

        var state = sim.State;
        var nibbit = state.Monsters[0];
        Sts2Sim.Core.Combat.Pipeline.PowerCmd.Apply<Sts2Sim.Core.Powers.StrengthPower>(state, nibbit, 3, nibbit, null);
        Sts2Sim.Core.Combat.Pipeline.PowerCmd.Apply<Sts2Sim.Core.Powers.VulnerablePower>(state, state.Player, 2, nibbit, null);
        var p = SimProjector.Project(state);
        Assert.SequenceEqual(new[] { new PowerProjection("VULNERABLE_POWER", 2) }, p.Player.Powers);
        Assert.SequenceEqual(new[] { new PowerProjection("STRENGTH_POWER", 3) }, p.Enemies[0].Powers);
        Assert.Equal((int)((13 + 3) * 1.5m), p.Enemies[0].Intents[0].AttackValue, "intent damage is the displayed, modified value");
    }

    public static void Test_Diff_OffsetsPullTheNamedStreamKTimes()
    {
        var set = RngSetup.Create(RngSetup.SeedingXxh64, TestSeed);
        var fresh = new RunRngSet(TestSeed);
        Assert.Equal(fresh.Seed, set.Seed);
        RngSetup.ApplyOffsets(set, new[]
        {
            new KeyValuePair<string, int>("Niche", 6),
            new KeyValuePair<string, int>("monster_ai", 2),
            new KeyValuePair<string, int>("Shuffle", 0)
        });
        Assert.Equal(6, set.Niche.Counter);
        Assert.Equal(2, set.MonsterAi.Counter);
        var kernel = fresh.Niche.Kernel.DeepClone();
        for (int i = 0; i < 6; i++)
        {
            kernel.NextULong();
        }
        Assert.Equal(kernel.ToSnapshot(6), set.Niche.ToSnapshot(), "k pulls = k raw kernel steps, counter k");
        var expectedNiche = fresh.Niche.DeepClone();
        for (int i = 0; i < 6; i++)
        {
            expectedNiche.NextUnsignedLong();
        }
        Assert.Equal(expectedNiche.ToSnapshot(), set.Niche.ToSnapshot());
        foreach (RunRngType type in Enum.GetValues<RunRngType>())
        {
            if (type is RunRngType.Niche or RunRngType.MonsterAi)
            {
                continue;
            }
            Assert.Equal(fresh.Get(type).ToSnapshot(), set.Get(type).ToSnapshot(), $"{type} untouched");
        }
        Assert.True(RngSetup.TryParseStream("CombatTargets", out var t1) && t1 == RunRngType.CombatTargets);
        Assert.True(RngSetup.TryParseStream("combat_targets", out var t2) && t2 == RunRngType.CombatTargets);
        Assert.False(RngSetup.TryParseStream("combatTargets", out _));
        Assert.Throws<ArgumentException>(() => RngSetup.ApplyOffsets(set, new[] { new KeyValuePair<string, int>("Bogus", 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => RngSetup.ApplyOffsets(set, new[] { new KeyValuePair<string, int>("Niche", -1) }));
        Assert.Throws<ArgumentException>(() => RngSetup.Create(RngSetup.SeedingXxh64, "old" + TestSeed));
        Assert.Throws<ArgumentException>(() => RngSetup.Create("md5", TestSeed));
    }

    public static void Test_Diff_LegacySeedingMatchesLegacyKernel()
    {
        ulong signExtended = RngSetup.LegacyV18RunSeed(LegacySeed);
        Assert.Equal(0xFFFFFFFFCD2400A3UL, signExtended);
        var set = RngSetup.Create(RngSetup.SeedingLegacyV18, LegacySeed);
        Assert.Equal("old" + LegacySeed, set.StringSeed);
        Assert.Equal(3441688739UL, set.Seed);
        foreach (RunRngType type in Enum.GetValues<RunRngType>())
        {
            var expected = RngSeeds.LegacyV18Kernel(signExtended, RunRngSet.StreamName(type), 0).ToSnapshot(0);
            Assert.Equal(expected, set.Get(type).ToSnapshot(), $"{type} = legacy kernel at counter 0");
            Assert.Equal((int)type, set.AliasOf(type), $"{type} is its own stream");
        }
        var upFront = set.UpFront.ToSnapshot();
        Assert.Equal(new RngSnapshot(0, 0x44F4FA92E3AD0E7CUL, 0x26C50CBB5FDC84C4UL, 0x2CC9628CBEE2EF9EUL, 0x8146CA7C39AE53B1UL), upFront);
        Assert.Equal(new MegaRandom(2206941694UL).ToSnapshot(0), upFront);

        RngSetup.ApplyOffsets(set, new[] { new KeyValuePair<string, int>("Shuffle", 5) });
        Assert.Equal(new RngSnapshot(5, 0x5709160AE24945FDUL, 0x30C15BC24685E6D5UL, 0x85BBD4FE163A3169UL, 0xC07A7504695C8659UL), set.Shuffle.ToSnapshot(),
            "offset pulls on a legacy stream = LegacyV18Kernel(seed, \"shuffle\", 5)");

        var modern = RngSetup.Create(RngSetup.SeedingXxh64, LegacySeed);
        Assert.NotEqual(modern.Shuffle.ToSnapshot(), RngSetup.CreateLegacyV18(LegacySeed).Shuffle.ToSnapshot());
        Assert.Throws<ArgumentException>(() => RngSetup.Create(RngSetup.SeedingLegacyV18, "old" + LegacySeed));
    }

    public static void Test_Diff_BundleJsonRoundTripAndValidation()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, new[] { new KeyValuePair<string, int>("Niche", 1) }, 10, policySeed: 7UL);
        Assert.True(recorded.Decisions.Count > 3);
        var node = BundleNode(recorded);
        string text = node.ToJsonString();
        var parsed = BundleJson.Parse(text, "x.json");
        Assert.Equal("x.json", parsed.FilePath);
        Assert.Equal(recorded.Decisions.Count, parsed.Decisions.Count);
        Assert.Equal(1, parsed.RngOffsets.Single(kv => kv.Key == "Niche").Value);
        Assert.Equal(recorded.Outcome, parsed.Outcome);
        Assert.Equal(recorded.FinalHp, parsed.FinalHp);
        Assert.Equal(2, parsed.Source.Floor);
        Assert.SequenceEqual(recorded.Deck, parsed.Deck);
        Assert.Equal(0, parsed.IgnoredKeys.Count);
        Assert.Equal(DiffReport.Serialize(node), DiffReport.Serialize(BundleJson.Write(parsed)), "write(parse(x)) == x");
        for (int i = 0; i < parsed.Decisions.Count; i++)
        {
            Assert.Equal(0, ProjectionComparer.Compare(parsed.Decisions[i].PreState!, recorded.Decisions[i].PreState!).Count, $"decision {i}");
            Assert.Equal(recorded.Decisions[i].Action, parsed.Decisions[i].Action, $"decision {i} action");
        }

        void Rejects(Action<JsonObject> mutate, string expectedPath)
        {
            var copy = BundleNode(recorded);
            mutate(copy);
            var ex = Assert.Throws<BundleFormatException>(() => Reparse(copy));
            Assert.Equal(expectedPath, ex.Path, ex.Message);
        }
        Rejects(j => j["bundle_version"] = 2, "$.bundle_version");
        Rejects(j => j["seeding"] = "md5", "$.seeding");
        Rejects(j => j.Remove("decisions"), "$");
        Rejects(j => j["decisions"]![0]!["index"] = 5, "$.decisions[0].index");
        Rejects(j => j["decisions"]![0]!["action"] = new JsonObject { ["type"] = "use_potion" }, "$.decisions[0].action.type");
        Rejects(j => j["rng_offsets"]!["Bogus"] = 1, "$.rng_offsets.Bogus");
        Rejects(j => j["rng_offsets"]!["Niche"] = -1, "$.rng_offsets.Niche");
        Rejects(j => j["decisions"]![0]!["pre_state"]!["player"]!["hand"]![0]!["cost"] = "Y", "$.decisions[0].pre_state.player.hand[0].cost");
        Rejects(j => j["decisions"]![0]!["pre_state"]!["player"]!["hp"] = 1.5, "$.decisions[0].pre_state.player.hp");
        Rejects(j => j["outcome"] = "draw", "$.outcome");
        Rejects(j => j["deck"]![0]!.AsObject().Remove("upgraded"), "$.deck[0]");

        var extra = BundleNode(recorded);
        extra["exporter_notes"] = "hello";
        extra["decisions"]![1]!["pre_state"]!["player"]!["stars"] = 0;
        var tolerant = Reparse(extra);
        Assert.SequenceEqual(new[] { "$.decisions[*].pre_state.player.stars", "$.exporter_notes" }, tolerant.IgnoredKeys);
        Assert.False(BundleJson.LooksLikeBundle(System.Text.Json.JsonDocument.Parse("{\"reasons\":[]}").RootElement));
    }

    public static void Test_Diff_SelfReplayBundlesAgree()
    {
        var bundles = new List<DiffBundle>
        {
            RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 1UL),
            RecordBundle(RngSetup.SeedingXxh64, "Q3X6PF8S37GA", new[] { new KeyValuePair<string, int>("Niche", 7) }, 0, policySeed: 2UL),
            RecordBundle(RngSetup.SeedingLegacyV18, LegacySeed, new[] { new KeyValuePair<string, int>("Niche", 1) }, 5, policySeed: 3UL),
            RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: null)
        };
        foreach (var recorded in bundles)
        {
            var result = BundleRunner.Run(Reparse(BundleNode(recorded), recorded.FilePath));
            Assert.Equal(BundleStatus.Agree, result.Status, $"{recorded.FilePath}: {result.Reason} {result.Divergence?.First}");
            Assert.Equal(recorded.Decisions.Count, result.DecisionsTotal);
            Assert.Equal(recorded.Decisions.Count, result.DecisionsCompared);
            Assert.Equal(recorded.Decisions.Count, result.DecisionsAgreed);
            Assert.Equal(0, result.SoftDiffs.Count);
            Assert.True(result.OutcomeChecked && result.OutcomeAgreed);
            Assert.Equal(recorded.Outcome, result.SimOutcome);
        }
        Assert.Equal(DiffBundle.OutcomeWin, bundles[3].Outcome, "the greedy attacker finishes the fight");

        var shifted = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 1UL);
        var wrongOffset = BundleNode(shifted);
        wrongOffset["rng_offsets"]!["Shuffle"] = 1;
        var offsetResult = BundleRunner.Run(Reparse(wrongOffset));
        Assert.Equal(BundleStatus.HardDivergence, offsetResult.Status, "a shifted Shuffle stream deals a different opening hand");
        Assert.Equal(0, offsetResult.Divergence!.DecisionIndex);
        Assert.Equal(DivergenceKind.State, offsetResult.Divergence.Kind);
    }

    public static void Test_Diff_InjectedDivergencesAreClassified()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 11UL);
        Assert.True(recorded.Decisions.Count >= 6, $"need a longer recording, got {recorded.Decisions.Count}");
        const int k = 3;

        var hard = BundleNode(recorded);
        Bump(hard["decisions"]![k]!["pre_state"]!["player"], "hp");
        var hardResult = BundleRunner.Run(Reparse(hard, "hard.json"));
        Assert.Equal(BundleStatus.HardDivergence, hardResult.Status);
        var d = hardResult.Divergence!;
        Assert.Equal(DivergenceKind.State, d.Kind);
        Assert.Equal(k, d.DecisionIndex);
        Assert.Equal("player.hp", d.First.Path);
        Assert.Equal(d.First.Sim!.GetValue<int>() + 1, d.First.Oracle!.GetValue<int>());
        Assert.Equal(k + 1, hardResult.DecisionsCompared);
        Assert.Equal(k, hardResult.DecisionsAgreed);
        Assert.NotNull(d.OracleProjection);
        Assert.NotNull(d.SimProjection);
        var repro = DiffReport.DivergenceJson(hardResult);
        Assert.Equal("hard.json", repro["bundle"]!.GetValue<string>());
        Assert.Equal("player.hp", repro["field_path"]!.GetValue<string>());
        Assert.Equal(k, repro["actions_prefix"]!.AsArray().Count);
        Assert.Equal(TestSeed, repro["setup"]!["seed"]!.GetValue<string>());
        Assert.Equal(11, repro["setup"]!["deck"]!.AsArray().Count);
        Assert.NotNull(repro["oracle_projection"]);
        Assert.NotNull(repro["sim_projection"]);

        var soft = BundleNode(recorded);
        soft["decisions"]![k]!["pre_state"]!["player"]!["discard"] = new JsonArray("NOT_A_CARD");
        var softResult = BundleRunner.Run(Reparse(soft));
        Assert.Equal(BundleStatus.Agree, softResult.Status, "soft divergences never stop a replay");
        Assert.Equal(1, softResult.SoftDiffs.Count);
        Assert.Equal(k, softResult.SoftDiffs[0].DecisionIndex);
        Assert.Equal("player.discard", softResult.SoftDiffs[0].Diff.Path);
        Assert.Equal(recorded.Decisions.Count, softResult.DecisionsAgreed);

        int targeted = recorded.Decisions.ToList().FindIndex(x => x.Action.TargetCombatId != null);
        Assert.True(targeted >= 0, "the recording plays a targeted card");
        var action = BundleNode(recorded);
        action["decisions"]![targeted]!["action"]!["target_combat_id"] = 99;
        var actionResult = BundleRunner.Run(Reparse(action));
        Assert.Equal(BundleStatus.HardDivergence, actionResult.Status);
        Assert.Equal(DivergenceKind.Action, actionResult.Divergence!.Kind);
        Assert.Equal(targeted, actionResult.Divergence.DecisionIndex);
        Assert.Equal("action.target_combat_id", actionResult.Divergence.First.Path);
        Assert.Equal(targeted, actionResult.DecisionsAgreed);

        int play = recorded.Decisions.ToList().FindIndex(x => x.Action.Type == BundleActionType.PlayCard);
        var inconsistent = BundleNode(recorded);
        inconsistent["decisions"]![play]!["action"]!["card_id"] = "NOT_A_CARD";
        var inconsistentResult = BundleRunner.Run(Reparse(inconsistent));
        Assert.Equal(BundleStatus.Error, inconsistentResult.Status);
        Assert.True(inconsistentResult.Reason!.StartsWith("bundle inconsistent", StringComparison.Ordinal), inconsistentResult.Reason);
    }

    public static void Test_Diff_UnobservedPreStatesAreReplayedButNotCompared()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 13UL);
        Assert.True(recorded.Decisions.Count >= 4);
        var node = BundleNode(recorded);
        node["decisions"]![1]!["pre_state"] = null;
        node["decisions"]![2]!.AsObject().Remove("pre_state");
        var parsed = Reparse(node);
        Assert.False(parsed.Decisions[1].IsObserved);
        Assert.False(parsed.Decisions[2].IsObserved);
        Assert.True(parsed.Decisions[3].IsObserved);
        Assert.True(BundleJson.Write(parsed)["decisions"]![1]!["pre_state"] is null, "an unobserved pre_state is written back as null");

        var result = BundleRunner.Run(parsed);
        Assert.Equal(BundleStatus.Agree, result.Status, result.Reason);
        Assert.Equal(2, result.DecisionsUnobserved);
        Assert.Equal(recorded.Decisions.Count - 2, result.DecisionsCompared);
        Assert.Equal(recorded.Decisions.Count - 2, result.DecisionsAgreed);
        Assert.True(result.OutcomeAgreed);
        Assert.Equal(2L, DiffReport.Overall(new[] { result }).DecisionsUnobserved);

        var corrupted = BundleNode(recorded);
        corrupted["decisions"]![1]!["pre_state"] = null;
        Bump(corrupted["decisions"]![2]!["pre_state"]!["player"], "hp");
        var later = BundleRunner.Run(Reparse(corrupted));
        Assert.Equal(BundleStatus.HardDivergence, later.Status);
        Assert.Equal(2, later.Divergence!.DecisionIndex, "the next observed state still catches a divergence");
    }

    public static void Test_Diff_ActionMappingByCombatId()
    {
        CombatSim? sim = null;
        int attackIndex = -1;
        for (int s = 0; s < 64 && attackIndex < 0; s++)
        {
            sim = OpeningSim(10, $"MAPPING{s:D5}");
            attackIndex = sim.State.Player.Hand.Cards.ToList().FindIndex(c => c.Id == "STRIKE_IRONCLAD");
        }
        Assert.True(attackIndex >= 0);
        var state = sim!.State;
        Assert.True(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, "STRIKE_IRONCLAD", false, 1), out var mapped, out var diff));
        Assert.Null(diff);
        Assert.Equal(GameAction.Play(attackIndex, 0), mapped);
        Assert.True(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, null, null, 1), out mapped, out _), "card_id/upgraded are optional cross-checks");

        Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, "BASH", false, 1), out _, out diff));
        Assert.Equal("action.card_id", diff!.Path);
        Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, "STRIKE_IRONCLAD", true, 1), out _, out diff));
        Assert.Equal("action.upgraded", diff!.Path);
        Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, "STRIKE_IRONCLAD", false, null), out _, out diff));
        Assert.Equal("action.target_combat_id", diff!.Path);
        Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(attackIndex, "STRIKE_IRONCLAD", false, 0), out _, out diff), "the player is not an enemy target");
        Assert.Equal("action.target_combat_id", diff!.Path);
        Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(9, null, null, 1), out _, out diff));
        Assert.Equal("action.hand_index", diff!.Path);

        int bane = state.Player.Hand.Cards.ToList().FindIndex(c => c.Id == "ASCENDERS_BANE");
        int defend = state.Player.Hand.Cards.ToList().FindIndex(c => c.Id == "DEFEND_IRONCLAD");
        if (defend >= 0)
        {
            Assert.True(BundleRunner.TryMapAction(state, BundleAction.Play(defend, "DEFEND_IRONCLAD", false, 1), out mapped, out _), "a stray target on an untargeted card is ignored");
            Assert.Equal(GameAction.Play(defend), mapped);
        }
        if (bane >= 0)
        {
            Assert.False(BundleRunner.TryMapAction(state, BundleAction.Play(bane, "ASCENDERS_BANE", false, null), out _, out diff));
            Assert.Equal("action.legal", diff!.Path);
        }
        Assert.True(BundleRunner.TryMapAction(state, BundleAction.EndTurn, out mapped, out _));
        Assert.Equal(GameAction.EndTurn, mapped);
    }

    public static void Test_Diff_OutcomeAndTerminalChecks()
    {
        var won = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: null);
        Assert.Equal(DiffBundle.OutcomeWin, won.Outcome);

        var wrongOutcome = BundleNode(won);
        wrongOutcome["outcome"] = DiffBundle.OutcomeLoss;
        var r1 = BundleRunner.Run(Reparse(wrongOutcome));
        Assert.Equal(BundleStatus.HardDivergence, r1.Status);
        Assert.Equal(DivergenceKind.Final, r1.Divergence!.Kind);
        Assert.Equal(won.Decisions.Count, r1.Divergence.DecisionIndex);
        Assert.Equal("outcome", r1.Divergence.First.Path);
        Assert.Equal(won.Decisions.Count, r1.DecisionsAgreed, "decision agreement is kept apart from the final check");
        Assert.True(r1.OutcomeChecked);
        Assert.False(r1.OutcomeAgreed);

        var wrongHp = BundleNode(won);
        Bump(wrongHp, "final_hp", -1);
        var r2 = BundleRunner.Run(Reparse(wrongHp));
        Assert.Equal("final_hp", r2.Divergence!.First.Path);

        var noHp = BundleNode(won);
        noHp["final_hp"] = null;
        Assert.Equal(BundleStatus.Agree, BundleRunner.Run(Reparse(noHp)).Status, "final_hp is optional");

        var extraDecision = BundleNode(won);
        var decisions = extraDecision["decisions"]!.AsArray();
        var copy = decisions[^1]!.DeepClone().AsObject();
        copy["index"] = decisions.Count;
        decisions.Add(copy);
        var r3 = BundleRunner.Run(Reparse(extraDecision));
        Assert.Equal(DivergenceKind.Terminal, r3.Divergence!.Kind);
        Assert.Equal(won.Decisions.Count, r3.Divergence.DecisionIndex);
        Assert.Equal("sim.outcome", r3.Divergence.First.Path);

        var abandoned = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 5UL, maxDecisions: 4);
        Assert.Equal(DiffBundle.OutcomeAbandoned, abandoned.Outcome);
        Assert.Equal(BundleStatus.Agree, BundleRunner.Run(Reparse(BundleNode(abandoned))).Status, "an abandoned attempt ends in progress");
    }

    public static void Test_Diff_IneligibleBundlesAreSkipped()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 4UL, maxDecisions: 3);
        void Skips(Action<JsonObject> mutate, string reasonFragment)
        {
            var node = BundleNode(recorded);
            mutate(node);
            var result = BundleRunner.Run(Reparse(node));
            Assert.Equal(BundleStatus.Skipped, result.Status, reasonFragment);
            Assert.True(result.Reason!.Contains(reasonFragment, StringComparison.Ordinal), result.Reason);
            Assert.Equal(0, result.DecisionsCompared);
        }
        Skips(j => j["character"] = "SILENT", "character SILENT");
        Skips(j => j["seed"] = "old" + TestSeed, "'old' prefix");
        Skips(j => j["encounter_id"] = "NOT_AN_ENCOUNTER", "encounter NOT_AN_ENCOUNTER");
        Skips(j => j["deck"]![0]!["id"] = "NOT_A_CARD", "card NOT_A_CARD");
        Skips(j => j["relics"]!.AsArray().Add("BOOMING_CONCH"), "relic BOOMING_CONCH");

        var badHp = BundleNode(recorded);
        badHp["player"]!["hp"] = 0;
        var error = BundleRunner.Run(Reparse(badHp));
        Assert.Equal(BundleStatus.Error, error.Status);
    }

    // Exporter extensions (sim/specs/diff-bundle-exporter.md §2, diff-harness.md §5): `decisions[].oracle` is known
    // provenance (not an unrecognized key), kept for round trips and divergence repros, never compared.
    public static void Test_Diff_ExporterOracleProvenanceIsRecognizedAndKept()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 41UL);
        Assert.True(recorded.Decisions.Count >= 4);
        var node = BundleNode(recorded);
        var decisions = node["decisions"]!.AsArray();
        for (int i = 0; i < decisions.Count; i++)
        {
            decisions[i]![BundleJson.OracleKey] = new JsonObject
            {
                ["action_seq"] = 100 + 2 * i,
                ["state_seq"] = 99 + 2 * i,
                ["settled"] = i != 1,
                ["pending_events"] = new JsonArray(),
                ["combat_card_index"] = i
            };
        }
        decisions[1]![BundleJson.OracleKey]!["rolled_forward"] = true;
        var parsed = Reparse(node);
        Assert.Equal(0, parsed.IgnoredKeys.Count, string.Join(", ", parsed.IgnoredKeys));
        Assert.Equal(99 + 2 * 3, parsed.Decisions[3].OracleJson!["state_seq"]!.GetValue<int>());
        Assert.Equal(DiffReport.Serialize(node), DiffReport.Serialize(BundleJson.Write(parsed)), "write(parse(x)) == x with provenance");
        Assert.Equal(BundleStatus.Agree, BundleRunner.Run(parsed).Status, "provenance is never compared");

        var unknown = BundleNode(recorded);
        unknown["decisions"]![0]!["exporter_debug"] = 1;
        Assert.SequenceEqual(new[] { "$.decisions[*].exporter_debug" }, Reparse(unknown).IgnoredKeys, "other unknown decision keys are still reported");

        const int k = 2;
        Bump(node["decisions"]![k]!["pre_state"]!["player"], "hp");
        var diverged = BundleRunner.Run(Reparse(node, "provenance.json"));
        Assert.Equal(BundleStatus.HardDivergence, diverged.Status);
        Assert.Equal(k, diverged.Divergence!.DecisionIndex);
        var repro = DiffReport.DivergenceJson(diverged);
        Assert.Equal(99 + 2 * k, repro["oracle_provenance"]!["state_seq"]!.GetValue<int>(), "the repro points at the recorder snapshot");
        Assert.Equal(100 + 2 * k, repro["oracle_provenance"]!["action_seq"]!.GetValue<int>());

        var plain = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 41UL);
        var plainNode = BundleNode(plain);
        Assert.False(plainNode["decisions"]![0]!.AsObject().ContainsKey(BundleJson.OracleKey), "no provenance is invented");
        Bump(plainNode["decisions"]![k]!["pre_state"]!["player"], "hp");
        var plainRepro = DiffReport.DivergenceJson(BundleRunner.Run(Reparse(plainNode)));
        Assert.False(plainRepro.ContainsKey("oracle_provenance"));
    }

    // `ineligible_reasons` (written by the exporter only into its ineligible/ bundles) carries reasons the bundle body
    // cannot show; the harness skips such bundles unless --include-ineligible, after its own sim gate.
    public static void Test_Diff_ExporterIneligibleBundlesAreSkippedUnlessIncluded()
    {
        var recorded = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 43UL);
        var node = BundleNode(recorded);
        node[BundleJson.IneligibleReasonsKey] = new JsonArray("deck_mismatch:missing=SETUP_STRIKE", "pre_state_hand_unsettled");
        var parsed = Reparse(node, "ineligible.json");
        Assert.SequenceEqual(new[] { "deck_mismatch:missing=SETUP_STRIKE", "pre_state_hand_unsettled" }, parsed.IneligibleReasons);
        Assert.Equal(0, parsed.IgnoredKeys.Count);
        Assert.Equal(DiffReport.Serialize(node), DiffReport.Serialize(BundleJson.Write(parsed)), "the verdict round-trips");

        var skipped = BundleRunner.Run(parsed);
        Assert.Equal(BundleStatus.Skipped, skipped.Status);
        Assert.True(skipped.Reason!.Contains("deck_mismatch:missing=SETUP_STRIKE; pre_state_hand_unsettled", StringComparison.Ordinal), skipped.Reason);
        Assert.Equal(0, skipped.DecisionsCompared);
        var included = BundleRunner.Run(parsed, includeExporterIneligible: true);
        Assert.Equal(BundleStatus.Agree, included.Status, included.Reason);
        Assert.Equal(recorded.Decisions.Count, included.DecisionsAgreed);
        Assert.Null(BundleRunner.IneligibleReason(parsed, includeExporterIneligible: true));

        var silent = BundleNode(recorded);
        silent[BundleJson.IneligibleReasonsKey] = new JsonArray("character_not_ported:SILENT");
        silent["character"] = "SILENT";
        var gate = BundleRunner.Run(Reparse(silent), includeExporterIneligible: true);
        Assert.Equal(BundleStatus.Skipped, gate.Status, "the flag never bypasses the sim gate");
        Assert.True(gate.Reason!.Contains("character SILENT", StringComparison.Ordinal), gate.Reason);

        var empty = BundleNode(recorded);
        empty[BundleJson.IneligibleReasonsKey] = new JsonArray();
        Assert.Equal(BundleStatus.Agree, BundleRunner.Run(Reparse(empty)).Status, "an empty verdict is eligible");
        var nulled = BundleNode(recorded);
        nulled[BundleJson.IneligibleReasonsKey] = null;
        Assert.Equal(0, Reparse(nulled).IneligibleReasons.Count);
        var bad = BundleNode(recorded);
        bad[BundleJson.IneligibleReasonsKey] = 5;
        Assert.Equal("$.ineligible_reasons", Assert.Throws<BundleFormatException>(() => Reparse(bad)).Path);

        string dir = Path.Combine(Path.GetTempPath(), "sts2sim-diff-ineligible-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stdout = Console.Out;
        var stderr = Console.Error;
        try
        {
            File.WriteAllText(Path.Combine(dir, "ineligible.json"), node.ToJsonString());
            string reportPath = Path.Combine(dir, "out", "report.json");
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            int byDefault = Program.Main(new[] { dir, "--report", reportPath });
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, byDefault, "a skipped bundle is not a failure");
            var report = JsonNode.Parse(File.ReadAllText(reportPath))!;
            Assert.Equal(1, report["summary"]!["skipped"]!.GetValue<int>());
            Assert.False(report["options"]!["include_ineligible"]!.GetValue<bool>());

            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            int withFlag = Program.Main(new[] { dir, "--include-ineligible", "--report", reportPath });
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, withFlag);
            var report2 = JsonNode.Parse(File.ReadAllText(reportPath))!;
            Assert.Equal(1, report2["summary"]!["agree"]!.GetValue<int>());
            Assert.True(report2["options"]!["include_ineligible"]!.GetValue<bool>());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Directory.Delete(dir, recursive: true);
        }
    }

    public static void Test_Diff_ReportAggregatesPerVersion()
    {
        var agree = BundleRunner.Run(Reparse(BundleNode(RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 21UL)), "a.json"));
        var oldVersion = BundleNode(RecordBundle(RngSetup.SeedingLegacyV18, LegacySeed, NoOffsets, 10, policySeed: 22UL, gameVersion: "v0.107.1"));
        Bump(oldVersion["decisions"]![1]!["pre_state"]!["player"], "block");
        var diverge = BundleRunner.Run(Reparse(oldVersion, "b.json"));
        var skippedNode = BundleNode(RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 23UL, maxDecisions: 2));
        skippedNode["encounter_id"] = "NOT_AN_ENCOUNTER";
        var skipped = BundleRunner.Run(Reparse(skippedNode, "c.json"));
        var results = new List<BundleResult> { skipped, diverge, agree };

        var overall = DiffReport.Overall(results);
        Assert.Equal(3, overall.Bundles);
        Assert.Equal(1, overall.Agree);
        Assert.Equal(1, overall.HardDivergence);
        Assert.Equal(1, overall.Skipped);
        Assert.Equal(agree.DecisionsTotal + diverge.DecisionsTotal, overall.DecisionsTotal, "skipped bundles stay out of the decision totals");
        Assert.Equal(agree.DecisionsCompared + 2, overall.DecisionsCompared);
        Assert.Equal(agree.DecisionsAgreed + 1, overall.DecisionsAgreed);

        var byVersion = DiffReport.ByVersion(results);
        Assert.SequenceEqual(new[] { "v0.107.1", "v0.111.0" }, byVersion.Keys);
        Assert.Equal(1, byVersion["v0.107.1"].HardDivergence);
        Assert.Equal(1L, byVersion["v0.107.1"].DecisionsAgreed);
        Assert.Equal(2, byVersion["v0.111.0"].Bundles);

        var report = DiffReport.Build(results, new[] { new IgnoredFile("reasons.json", "no top-level bundle_version (not a diff bundle)") }, new[] { "bundles/" });
        var reparsed = JsonNode.Parse(DiffReport.Serialize(report))!;
        Assert.Equal(3, reparsed["summary"]!["bundles"]!.GetValue<int>());
        Assert.Equal(1, reparsed["divergences"]!.AsArray().Count);
        var div = reparsed["divergences"]![0]!;
        Assert.Equal("b.json", div["bundle"]!.GetValue<string>());
        Assert.Equal("player.block", div["field_path"]!.GetValue<string>());
        Assert.Equal(1, div["decision_index"]!.GetValue<int>());
        Assert.Equal(RngSetup.SeedingLegacyV18, div["setup"]!["seeding"]!.GetValue<string>());
        Assert.Equal(1, reparsed["skipped"]!.AsArray().Count);
        Assert.Equal(1, reparsed["ignored_files"]!.AsArray().Count);
        Assert.SequenceEqual(new[] { "a.json", "b.json", "c.json" }, reparsed["bundles"]!.AsArray().Select(b => b!["bundle"]!.GetValue<string>()));
    }

    public static void Test_Diff_CliRunsADirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sts2sim-diff-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stdout = Console.Out;
        var stderr = Console.Error;
        try
        {
            var good = RecordBundle(RngSetup.SeedingXxh64, TestSeed, NoOffsets, 10, policySeed: 31UL);
            File.WriteAllText(Path.Combine(dir, "good.json"), DiffReport.Serialize(BundleJson.Write(good)));
            File.WriteAllText(Path.Combine(dir, "reasons.json"), "{\"reasons\": {}}");
            string reportPath = Path.Combine(dir, "out", "report.json");
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            int code = Program.Main(new[] { dir, "--report", reportPath });
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, code);
            var report = JsonNode.Parse(File.ReadAllText(reportPath))!;
            Assert.Equal(1, report["summary"]!["agree"]!.GetValue<int>());
            Assert.Equal(1, report["ignored_files"]!.AsArray().Count);

            var bad = BundleNode(good);
            Bump(bad["decisions"]![0]!["pre_state"]!["player"], "energy");
            File.WriteAllText(Path.Combine(dir, "bad.json"), bad.ToJsonString());
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            int failing = Program.Main(new[] { dir, "--report", reportPath });
            int usage = Program.Main(Array.Empty<string>());
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(1, failing);
            Assert.Equal(2, usage);
            var report2 = JsonNode.Parse(File.ReadAllText(reportPath))!;
            Assert.Equal(2, report2["summary"]!["bundles"]!.GetValue<int>(), "the report file itself is not read back as input");
            Assert.Equal("player.energy", report2["divergences"]![0]!["field_path"]!.GetValue<string>());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Directory.Delete(dir, recursive: true);
        }
    }
}
