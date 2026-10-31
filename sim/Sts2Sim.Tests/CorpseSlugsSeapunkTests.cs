using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

public static class CorpseSlugsSeapunkTests
{
    private const int PlayerHp = 80;
    private const string Slugs = CorpseSlugsWeak.EncounterIdEntry;
    private const string Punk = SeapunkWeak.EncounterIdEntry;
    private const string Whip = CorpseSlug.WhipSlapMoveId;
    private const string Glomp = CorpseSlug.GlompMoveId;
    private const string Goop = CorpseSlug.GoopMoveId;
    private const string SeaKick = Seapunk.SeaKickMoveId;
    private const string Spin = Seapunk.SpinningKickMoveId;
    private const string Burp = Seapunk.BubbleBurpMoveId;
    private const string Stunned = MoveStateDef.StunnedMoveId;

    private static readonly string[] SlugCycle = { Whip, Glomp, Goop };

    public static void Test_CorpseSlugsSeapunk_IdsAndRegistry()
    {
        Assert.Equal("CORPSE_SLUG", CorpseSlug.MonsterIdEntry);
        Assert.Equal("SEAPUNK", Seapunk.MonsterIdEntry);
        Assert.Equal("CORPSE_SLUGS_WEAK", CorpseSlugsWeak.EncounterIdEntry);
        Assert.Equal("SEAPUNK_WEAK", SeapunkWeak.EncounterIdEntry);
        Assert.Equal("RAVENOUS_POWER", RavenousPower.PowerId);
        Assert.Equal(DeterministicHash.Slugify("CorpseSlug"), CorpseSlug.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("Seapunk"), Seapunk.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("CorpseSlugsWeak"), CorpseSlugsWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("SeapunkWeak"), SeapunkWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("RavenousPower"), RavenousPower.PowerId);

        Assert.True(MonsterRegistry.Get(CorpseSlug.MonsterIdEntry) is CorpseSlug);
        Assert.True(MonsterRegistry.Get(Seapunk.MonsterIdEntry) is Seapunk);
        Assert.True(EncounterRegistry.Get(Slugs) is CorpseSlugsWeak);
        Assert.True(EncounterRegistry.Get(Punk) is SeapunkWeak);
        foreach (var id in new[] { Slugs, Punk })
        {
            var encounter = EncounterRegistry.Get(id);
            Assert.True(encounter.IsWeak, id);
            Assert.Equal(0, encounter.Slots.Count, $"{id} defines no slots");
        }
        Assert.False(MonsterRegistry.Get(CorpseSlug.MonsterIdEntry).UsesPrivateRng);
        Assert.False(MonsterRegistry.Get(Seapunk.MonsterIdEntry).UsesPrivateRng);

        var power = PowerRegistry.Create(RavenousPower.PowerId);
        Assert.True(power is RavenousPower);
        Assert.Equal(PowerType.Buff, power.Type);
        Assert.Equal(PowerStackType.Counter, power.StackType);
        Assert.Equal(PowerInstanceType.None, power.InstanceType);
        Assert.False(power.AllowNegative);
        Assert.True(power.ShouldPowerBeRemovedAfterOwnerDeath());
    }

    public static void Test_CorpseSlugsSeapunk_HpRangesByAscension()
    {
        var slug = MonsterRegistry.Get(CorpseSlug.MonsterIdEntry);
        var punk = MonsterRegistry.Get(Seapunk.MonsterIdEntry);
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool tough = asc >= 8;
            Assert.Equal(tough ? 27 : 25, slug.MinInitialHp(cs), $"slug min A{asc}");
            Assert.Equal(tough ? 29 : 27, slug.MaxInitialHp(cs), $"slug max A{asc}");
            Assert.Equal(tough ? 47 : 44, punk.MinInitialHp(cs), $"seapunk min A{asc}");
            Assert.Equal(tough ? 49 : 46, punk.MaxInitialHp(cs), $"seapunk max A{asc}");
        }
    }

    public static void Test_CorpseSlugsSeapunk_SlugHpRollsAreUniqueNicheDrawsInCreationOrder()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int min = asc >= 8 ? 27 : 25;
            var firstValues = new HashSet<int>();
            var pairs = new HashSet<(int, int)>();
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var shared = new Rng(seed);
                var cs = NewCombat(asc, new RunRngSet(shared));
                var spawned = SpawnEncounter(cs, Slugs);

                var probe = new Rng(seed);
                int first = min + probe.NextInt(0, 3);
                var rest = new List<int>();
                for (int v = min; v <= min + 2; v++)
                {
                    if (v != first)
                    {
                        rest.Add(v);
                    }
                }
                int second = rest[probe.NextInt(0, rest.Count)];

                Assert.Equal(2, spawned.Count);
                Assert.Equal(1, spawned[0].CombatId, "creation order");
                Assert.Equal(2, spawned[1].CombatId, "creation order");
                Assert.Equal(first, spawned[0].MaxHp, $"A{asc} seed {seed} first slug");
                Assert.Equal(first, spawned[0].CurrentHp);
                Assert.Equal<int?>(first, spawned[0].MaxHpBeforeModification);
                Assert.Equal(second, spawned[1].MaxHp, $"A{asc} seed {seed} second slug");
                Assert.Equal(second, spawned[1].CurrentHp);
                Assert.Equal(2, shared.Counter, "one Niche draw per slug; the encounter Rng is a separate instance");
                firstValues.Add(first);
                pairs.Add((first, second));
            }
            Assert.Equal(3, firstValues.Count, $"every first-slug HP is reachable at A{asc}");
            Assert.Equal(6, pairs.Count, $"every ordered pair of distinct HPs is reachable at A{asc}");
        }
    }

    public static void Test_CorpseSlugsSeapunk_SeapunkHpRollIsOneNicheDraw()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int min = asc >= 8 ? 47 : 44;
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var shared = new Rng(seed);
                var cs = NewCombat(asc, new RunRngSet(shared));
                var spawned = SpawnEncounter(cs, Punk);
                int expected = min + new Rng(seed).NextInt(0, 3);
                Assert.Equal(1, spawned.Count);
                Assert.Equal(1, spawned[0].CombatId);
                Assert.Equal(expected, spawned[0].MaxHp, $"A{asc} seed {seed}");
                Assert.Equal(expected, spawned[0].CurrentHp);
                Assert.Equal(1, shared.Counter, "exactly one draw");
                seen.Add(expected);
            }
            Assert.Equal(3, seen.Count, $"every Seapunk HP is reachable at A{asc}");
        }
    }

    public static void Test_CorpseSlugsSeapunk_SpawnAdvancesOnlyNiche()
    {
        foreach (var (id, draws) in new[] { (Slugs, 2), (Punk, 1) })
        {
            const ulong seed = 4242UL;
            var cs = NewCombat(0, new RunRngSet(seed));
            var spawned = SpawnEncounter(cs, id);
            var niche = new RunRngSet(seed).Niche;
            int expectedFirst = MonsterRegistry.Get(spawned[0].MonsterId).MinInitialHp(cs) + niche.NextInt(0, 3);
            Assert.Equal(expectedFirst, spawned[0].MaxHp, id);
            foreach (var type in Enum.GetValues<RunRngType>())
            {
                Assert.Equal(type == RunRngType.Niche ? draws : 0, cs.Rng.Get(type).Counter, $"{id} {type}");
            }
        }
    }

    public static void Test_CorpseSlugsSeapunk_MoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool tough = asc >= 8;
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 9 : 8, CorpseSlug.GlompDamage(cs), $"glomp A{asc}");
            Assert.Equal(deadly ? 5 : 4, CorpseSlug.RavenousStrength(cs), $"ravenous A{asc}");
            Assert.Equal(deadly ? 13 : 11, Seapunk.SeaKickDamage(cs), $"sea kick A{asc}");
            Assert.Equal(tough ? 8 : 7, Seapunk.BubbleBlock(cs), $"bubble block A{asc}");
            Assert.Equal(deadly ? 2 : 1, Seapunk.BubbleStrength(cs), $"bubble strength A{asc}");
        }
        Assert.Equal(3, CorpseSlug.WhipSlapDamage);
        Assert.Equal(2, CorpseSlug.WhipSlapRepeat);
        Assert.Equal(2, CorpseSlug.GoopFrailAmount);
        Assert.Equal(2, Seapunk.SpinningKickDamage);
        Assert.Equal(4, Seapunk.SpinningKickRepeat);
    }

    public static void Test_CorpseSlugsSeapunk_SlugGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            var slug = SpawnSlug(cs, 0);
            var machine = slug.Ai.Machine;
            Assert.Equal(3, machine.States.Count);
            Assert.Equal(Whip, machine.InitialStateId);

            var whip = (MoveStateDef)machine.Get(Whip);
            var glomp = (MoveStateDef)machine.Get(Glomp);
            var goop = (MoveStateDef)machine.Get(Goop);
            Assert.Equal(Glomp, whip.FollowUpState?.Id);
            Assert.Equal(Goop, glomp.FollowUpState?.Id);
            Assert.Equal(Whip, goop.FollowUpState?.Id);
            foreach (var move in new[] { whip, glomp, goop })
            {
                Assert.False(move.MustPerformOnceBeforeTransitioning, move.Id);
                Assert.True(move.ShouldAppearInLogs, move.Id);
                Assert.Equal(1, move.Intents.Count, move.Id);
            }

            var targets = MonsterLifecycle.PlayerCreatures(cs);
            var whipIntent = (MultiAttackIntent)whip.Intents[0];
            Assert.Equal(IntentType.Attack, whipIntent.IntentType);
            Assert.Equal(3m, whipIntent.GetBaseDamage(cs, slug));
            Assert.Equal(2, whipIntent.GetRepeats(cs, slug));
            Assert.Equal(6, whipIntent.GetTotalDamage(cs, targets, slug));

            var glompIntent = (SingleAttackIntent)glomp.Intents[0];
            Assert.Equal(IntentType.Attack, glompIntent.IntentType);
            Assert.Equal(asc >= 9 ? 9m : 8m, glompIntent.GetBaseDamage(cs, slug));
            Assert.Equal(1, glompIntent.GetRepeats(cs, slug));

            Assert.Equal(IntentType.Debuff, goop.Intents[0].IntentType, "GOOP shows a plain (not strong) debuff");
            Assert.True(whip.IntendsToAttack);
            Assert.True(glomp.IntendsToAttack);
            Assert.False(goop.IntendsToAttack);
        }
    }

    public static void Test_CorpseSlugsSeapunk_SlugInitialMoveFollowsStarterIndex()
    {
        var cases = new (int Index, string Move)[]
        {
            (0, Whip), (1, Glomp), (2, Goop), (3, Whip), (4, Glomp), (5, Goop), (-1, Goop), (-2, Goop), (-3, Whip)
        };
        foreach (var (index, move) in cases)
        {
            var shared = new Rng(5UL);
            var cs = NewCombat(0, new RunRngSet(shared));
            var slug = SpawnSlug(cs, index);
            int before = shared.Counter;
            Assert.Equal(move, slug.Ai.CurrentStateId, $"starter index {index}");
            Assert.SequenceEqual(new[] { move }, slug.Ai.StateLog, "the initial move is logged when the machine is built");
            MonsterLifecycle.AfterCreatureAdded(cs, slug);
            Assert.Equal(move, slug.Ai.NextMove?.Id, $"starter index {index}");
            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            Assert.Equal(move, slug.Ai.NextMove?.Id, "the round-1 re-roll keeps the first move");
            Assert.Equal(before, shared.Counter, "the slug AI draws nothing");
        }
    }

    public static void Test_CorpseSlugsSeapunk_SeapunkGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            var punk = SpawnEncounter(cs, Punk)[0];
            Assert.Null(punk.Extra);
            var machine = punk.Ai.Machine;
            Assert.Equal(3, machine.States.Count);
            Assert.Equal(SeaKick, machine.InitialStateId);

            var kick = (MoveStateDef)machine.Get(SeaKick);
            var spin = (MoveStateDef)machine.Get(Spin);
            var burp = (MoveStateDef)machine.Get(Burp);
            Assert.Equal(Spin, kick.FollowUpState?.Id);
            Assert.Equal(Burp, spin.FollowUpState?.Id);
            Assert.Equal(SeaKick, burp.FollowUpState?.Id);

            var targets = MonsterLifecycle.PlayerCreatures(cs);
            var kickIntent = (SingleAttackIntent)kick.Intents[0];
            Assert.Equal(1, kick.Intents.Count);
            Assert.Equal(asc >= 9 ? 13m : 11m, kickIntent.GetBaseDamage(cs, punk));
            var spinIntent = (MultiAttackIntent)spin.Intents[0];
            Assert.Equal(1, spin.Intents.Count);
            Assert.Equal(2m, spinIntent.GetBaseDamage(cs, punk));
            Assert.Equal(4, spinIntent.GetRepeats(cs, punk));
            Assert.Equal(8, spinIntent.GetTotalDamage(cs, targets, punk));
            Assert.Equal(2, burp.Intents.Count);
            Assert.Equal(IntentType.Buff, burp.Intents[0].IntentType);
            Assert.Equal(IntentType.Defend, burp.Intents[1].IntentType);
            Assert.True(kick.IntendsToAttack);
            Assert.True(spin.IntendsToAttack);
            Assert.False(burp.IntendsToAttack);
        }
    }

    public static void Test_CorpseSlugsSeapunk_EncounterDrawsOneStarterIndexForBothSlugs()
    {
        var encounter = EncounterRegistry.Get(Slugs);
        var seen = new HashSet<int>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var cs = NewCombat(0, seed);
            var rng = new Rng(seed * 7919UL);
            var spawns = encounter.GenerateMonsters(cs, rng);
            Assert.Equal(1, rng.Counter, "one NextInt(3) for the whole encounter");
            int k = new Rng(seed * 7919UL).NextInt(3);
            seen.Add(k);

            Assert.Equal(2, spawns.Count);
            Assert.Null(spawns[0].SlotName);
            Assert.Null(spawns[1].SlotName);
            Assert.True(spawns[0].Def is CorpseSlug && spawns[1].Def is CorpseSlug);
            var first = SpawnAndAdd(cs, spawns[0]);
            var second = SpawnAndAdd(cs, spawns[1]);
            Assert.Equal(k, CorpseSlug.GetExtra(first).StarterMoveIdx, $"seed {seed}");
            Assert.Equal((k + 1) % 3, CorpseSlug.GetExtra(second).StarterMoveIdx, $"seed {seed}");
            Assert.Equal(SlugCycle[k], first.Ai.CurrentStateId);
            Assert.Equal(SlugCycle[(k + 1) % 3], second.Ai.CurrentStateId);
            Assert.False(CorpseSlug.GetExtra(first).IsRavenous);
        }
        Assert.Equal(3, seen.Count, "every starter index occurs");
        Assert.Throws<InvalidOperationException>(() => encounter.GenerateMonsters(NewCombat(0, 1UL)));

        var seapunk = EncounterRegistry.Get(Punk);
        var probe = new Rng(3UL);
        var viaRng = seapunk.GenerateMonsters(NewCombat(0, 1UL), probe);
        var plain = seapunk.GenerateMonsters(NewCombat(0, 1UL));
        Assert.Equal(0, probe.Counter, "SEAPUNK_WEAK draws nothing from its encounter Rng");
        Assert.Equal(1, viaRng.Count);
        Assert.Equal(1, plain.Count);
        Assert.True(ReferenceEquals(MonsterRegistry.Get(Seapunk.MonsterIdEntry), viaRng[0].Def));
        Assert.Null(viaRng[0].SlotName);
        Assert.Null(viaRng[0].Configure);
    }

    public static void Test_CorpseSlugsSeapunk_StarterAssignmentSkipsOtherMonstersAndChainsConfigure()
    {
        var slugDef = MonsterRegistry.Get(CorpseSlug.MonsterIdEntry);
        var punkSpawn = new MonsterSpawn(MonsterRegistry.Get(Seapunk.MonsterIdEntry));
        var preset = new MonsterSpawn(slugDef, "front", static m => CorpseSlug.GetExtra(m).IsRavenous = true);
        var input = new[] { new MonsterSpawn(slugDef), punkSpawn, preset, new MonsterSpawn(slugDef) };
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var rng = new Rng(seed);
            var result = CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(input, rng);
            Assert.Equal(1, rng.Counter);
            int k = new Rng(seed).NextInt(3);
            Assert.Equal(4, result.Length);
            Assert.True(ReferenceEquals(punkSpawn, result[1]), "a spawn that is not a CorpseSlug passes through unchanged");
            Assert.Equal("front", result[2].SlotName);

            var cs = NewCombat(0, seed);
            var a = SpawnAndAdd(cs, result[0]);
            var c = SpawnAndAdd(cs, result[2]);
            var d = SpawnAndAdd(cs, result[3]);
            Assert.Equal(k, CorpseSlug.GetExtra(a).StarterMoveIdx);
            Assert.Equal((k + 1) % 3, CorpseSlug.GetExtra(c).StarterMoveIdx, "the counter skips the Seapunk");
            Assert.Equal((k + 2) % 3, CorpseSlug.GetExtra(d).StarterMoveIdx);
            Assert.True(CorpseSlug.GetExtra(c).IsRavenous, "an existing Configure still runs");
            Assert.False(CorpseSlug.GetExtra(a).IsRavenous);
        }

        var empty = new Rng(11UL);
        Assert.Equal(0, CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(Array.Empty<MonsterSpawn>(), empty).Length);
        Assert.Equal(1, empty.Counter, "the draw happens before the monsters are enumerated");
    }

    public static void Test_CorpseSlugsSeapunk_RecordedSlugStartersMatchEncounterRng()
    {
        var cases = new (string Seed, RngSeedingScheme Scheme, int Floor, int Ascension, int Starter, string Recorded)[]
        {
            ("GM28D28DX794", RngSeedingScheme.Xxh64, 2, 6, 0, "v0.111.0 3x2 | 8"),
            ("NBNDT3S61AUM", RngSeedingScheme.Xxh64, 5, 4, 1, "v0.111.0 8 | Debuff"),
            ("EC039PTFQWV5", RngSeedingScheme.Xxh64, 2, 7, 2, "v0.111.0 Debuff | 3x2"),
            ("15TMTYNMWP0B", RngSeedingScheme.Xxh64, 3, 9, 1, "v0.110.1 9 | Debuff"),
            ("Q9UNSU305H87", RngSeedingScheme.Xxh64, 5, 9, 2, "v0.110.1 Debuff | 3x2"),
            ("QACFYUSXLF", RngSeedingScheme.LegacyV18, 2, 1, 0, "v0.107.1 3x2 | 8"),
            ("RWHGJL4NJ3", RngSeedingScheme.LegacyV18, 12, 1, 1, "v0.107.1 8 | Debuff"),
            ("Y323SU0BET", RngSeedingScheme.LegacyV18, 2, 2, 2, "v0.107.1 Debuff | 3x2"),
            ("H2Z5655063", RngSeedingScheme.LegacyV18, 2, 10, 0, "v0.107.1 3x2 | 9"),
        };
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        foreach (var c in cases)
        {
            string seedText = c.Scheme == RngSeedingScheme.LegacyV18 ? RngSeeds.OldSeedPrefix + c.Seed : c.Seed;
            var rng = new RunRngSet(seedText);
            var context = CombatRunContext.ForAct(1, c.Floor, seeding: c.Scheme);
            Assert.Equal(c.Starter, context.CreateEncounterRng(rng.Seed, Slugs).NextInt(3), $"{c.Seed} floor {c.Floor} (recorded {c.Recorded})");

            var sim = CombatSim.Create(rng, deck, Slugs, c.Ascension, null, null, context);
            var monsters = sim.State.Monsters;
            Assert.Equal(2, monsters.Count);
            Assert.Equal(SlugCycle[c.Starter], monsters[0].Ai.NextMove?.Id, $"{c.Seed} first slug (recorded {c.Recorded})");
            Assert.Equal(SlugCycle[(c.Starter + 1) % 3], monsters[1].Ai.NextMove?.Id, $"{c.Seed} second slug (recorded {c.Recorded})");
        }
    }

    public static void Test_CorpseSlugsSeapunk_RecordedFirstStatesMatch()
    {
        var cases = new (string Seed, string Seeding, int Ascension, int NicheOffset, string Encounter, (int Hp, string Move, int Value, int Times)[] Enemies)[]
        {
            ("GM28D28DX794", RngSetup.SeedingXxh64, 6, 0, Slugs, new[] { (26, Whip, 3, 2), (25, Glomp, 8, 1) }),
            ("EC039PTFQWV5", RngSetup.SeedingXxh64, 7, 0, Slugs, new[] { (27, Goop, 0, 0), (25, Whip, 3, 2) }),
            ("P2UZ19X1GGNQ", RngSetup.SeedingXxh64, 4, 7, Punk, new[] { (45, SeaKick, 11, 1) }),
            ("K4FB4086NTRE", RngSetup.SeedingXxh64, 7, 0, Punk, new[] { (46, SeaKick, 11, 1) }),
            ("QACFYUSXLF", RngSetup.SeedingLegacyV18, 1, 0, Slugs, new[] { (27, Whip, 3, 2), (26, Glomp, 8, 1) }),
            ("Z94LRG8T2Y", RngSetup.SeedingLegacyV18, 8, 0, Slugs, new[] { (28, Goop, 0, 0), (29, Whip, 3, 2) }),
            ("H2Z5655063", RngSetup.SeedingLegacyV18, 10, 6, Slugs, new[] { (28, Whip, 3, 2), (27, Glomp, 9, 1) }),
            ("VGRACWW9H8", RngSetup.SeedingLegacyV18, 10, 6, Slugs, new[] { (28, Glomp, 9, 1), (29, Goop, 0, 0) }),
            ("X20BJH1S4R", RngSetup.SeedingLegacyV18, 10, 0, Punk, new[] { (48, SeaKick, 13, 1) }),
            ("STFR70JJFB", RngSetup.SeedingLegacyV18, 10, 0, Punk, new[] { (49, SeaKick, 13, 1) }),
        };
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        foreach (var c in cases)
        {
            var rng = RngSetup.Create(c.Seeding, c.Seed);
            for (int i = 0; i < c.NicheOffset; i++)
            {
                rng.Niche.NextUnsignedLong();
            }
            var context = CombatRunContext.ForAct(1, 2, seeding: CombatRunContext.ParseSeeding(c.Seeding));
            var sim = CombatSim.Create(rng, deck, c.Encounter, c.Ascension, null, null, context);
            var cs = sim.State;
            var targets = MonsterLifecycle.PlayerCreatures(cs);
            Assert.Equal(c.Enemies.Length, cs.Monsters.Count, c.Seed);
            for (int i = 0; i < cs.Monsters.Count; i++)
            {
                var (hp, move, value, times) = c.Enemies[i];
                var m = cs.Monsters[i];
                string label = $"{c.Seed} {c.Seeding} A{c.Ascension} enemy {i + 1}";
                Assert.Equal(i + 1, m.CombatId, label);
                Assert.Equal(hp, m.MaxHp, label);
                Assert.Equal(hp, m.CurrentHp, label);
                Assert.Equal(move, m.Ai.NextMove?.Id, label);
                if (value > 0)
                {
                    var intent = (AttackIntent)m.Ai.NextMove!.Intents[0];
                    Assert.Equal(value, intent.GetSingleDamage(cs, targets, m), label);
                    Assert.Equal(times, intent.GetRepeats(cs, m), label);
                }
                if (c.Encounter == Slugs)
                {
                    Assert.Equal(c.Ascension >= 9 ? 5 : 4, m.GetPowerAmount<RavenousPower>(), label);
                }
                else
                {
                    Assert.Equal(0, m.Powers.Count, label);
                }
            }
        }
    }

    public static void Test_CorpseSlugsSeapunk_CombatSimSlugSetupOrderDrawsAndRavenous()
    {
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        foreach (int asc in new[] { 0, 9 })
        {
            int min = asc >= 8 ? 27 : 25;
            for (ulong seed = 1; seed <= 25; seed++)
            {
                var rng = new RunRngSet(seed);
                var sim = CombatSim.Create(rng, deck, Slugs, asc, null, null, CombatRunContext.Default);
                var cs = sim.State;
                Assert.Equal(2, cs.Monsters.Count);
                var a = cs.Monsters[0];
                var b = cs.Monsters[1];
                Assert.Equal(1, a.CombatId);
                Assert.Equal(2, b.CombatId);

                var niche = new RunRngSet(seed).Niche;
                int hpA = min + niche.NextInt(0, 3);
                var rest = new List<int>();
                for (int v = min; v <= min + 2; v++)
                {
                    if (v != hpA)
                    {
                        rest.Add(v);
                    }
                }
                int hpB = rest[niche.NextInt(0, rest.Count)];
                Assert.Equal(hpA, a.MaxHp, $"A{asc} seed {seed}");
                Assert.Equal(hpB, b.MaxHp, $"A{asc} seed {seed}");
                Assert.Equal(2, cs.Rng.Niche.Counter, "one Niche draw per slug, in creation order");
                Assert.Equal(0, cs.Rng.MonsterAi.Counter, "the first rolls draw nothing");
                Assert.Equal(0, cs.Rng.CombatTargets.Counter);

                int k = CombatRunContext.Default.CreateEncounterRng(rng.Seed, Slugs).NextInt(3);
                Assert.Equal(SlugCycle[k], a.Ai.NextMove?.Id);
                Assert.Equal(SlugCycle[(k + 1) % 3], b.Ai.NextMove?.Id);
                foreach (var m in new[] { a, b })
                {
                    var ravenous = Assert.NotNull(m.GetPower<RavenousPower>());
                    Assert.Equal(asc >= 9 ? 5 : 4, ravenous.Amount);
                    Assert.Equal(m.CombatId, ravenous.ApplierId);
                    Assert.Equal(1, m.Powers.Count);
                    Assert.False(m.IsStunned);
                }
            }
        }
    }

    public static void Test_CorpseSlugsSeapunk_SlugCyclesFromEachStarter()
    {
        for (int starter = 0; starter < 3; starter++)
        {
            var shared = new Rng(17UL);
            var cs = NewCombat(0, new RunRngSet(shared));
            var slug = SpawnSlug(cs, starter);
            var ai = slug.Ai;
            int before = shared.Counter;
            MonsterLifecycle.AfterCreatureAdded(cs, slug);
            var actual = new List<string>();
            for (int turn = 0; turn < 7; turn++)
            {
                if (turn > 0)
                {
                    MonsterLifecycle.PrepareForNextTurn(cs, slug);
                }
                var move = ai.NextMove!;
                actual.Add(move.Id);
                SimulatePerformed(ai, move);
            }
            var expected = Enumerable.Range(0, 7).Select(t => SlugCycle[(starter + t) % 3]).ToArray();
            Assert.SequenceEqual(expected, actual, $"starter {starter}");
            Assert.SequenceEqual(expected, ai.StateLog, $"starter {starter}");
            Assert.Equal(before, shared.Counter, "the slug AI consumes no randomness");
        }
    }

    public static void Test_CorpseSlugsSeapunk_SeapunkCycle()
    {
        var shared = new Rng(19UL);
        var cs = NewCombat(0, new RunRngSet(shared));
        var punk = SpawnEncounter(cs, Punk)[0];
        var ai = punk.Ai;
        int before = shared.Counter;
        MonsterLifecycle.AfterCreatureAdded(cs, punk);
        MonsterLifecycle.PrepareForNextTurn(cs, punk);
        var actual = new List<string>();
        for (int turn = 0; turn < 7; turn++)
        {
            if (turn > 0)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, punk);
            }
            var move = ai.NextMove!;
            actual.Add(move.Id);
            SimulatePerformed(ai, move);
        }
        var expected = new[] { SeaKick, Spin, Burp, SeaKick, Spin, Burp, SeaKick };
        Assert.SequenceEqual(expected, actual);
        Assert.SequenceEqual(expected, ai.StateLog);
        Assert.Equal(before, shared.Counter, "the Seapunk AI consumes no randomness");
    }

    public static void Test_CorpseSlugsSeapunk_SlugMovesDealDamageAndApplyFrail()
    {
        foreach (int asc in new[] { 0, 8, 9, 10 })
        {
            var cs = NewCombat(asc, 61UL);
            var slug = SpawnSlug(cs, 0);
            var player = cs.Player;
            int glomp = asc >= 9 ? 9 : 8;
            MonsterLifecycle.AfterCreatureAdded(cs, slug);
            Assert.Equal(asc >= 9 ? 5 : 4, slug.GetPowerAmount<RavenousPower>(), $"AfterAddedToRoom Ravenous A{asc}");
            MonsterLifecycle.OnSideSwitch(slug);

            MonsterLifecycle.TakeTurn(cs, slug);
            int hp = PlayerHp - 6;
            Assert.Equal(hp, player.CurrentHp, $"WHIP_SLAP 3x2 A{asc}");
            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            Assert.Equal(Glomp, slug.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, slug);
            hp -= glomp;
            Assert.Equal(hp, player.CurrentHp, $"GLOMP A{asc}");
            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            Assert.Equal(Goop, slug.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, slug);
            Assert.Equal(hp, player.CurrentHp, "GOOP deals no damage");
            var frail = Assert.NotNull(player.GetPower<FrailPower>());
            Assert.Equal(2, frail.Amount, $"GOOP Frail A{asc}");
            Assert.Equal(slug.CombatId, frail.ApplierId);
            Assert.Null(slug.GetPower<FrailPower>(), "Frail goes to the player");
            Assert.Equal(0, slug.Block);

            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            Assert.Equal(Whip, slug.Ai.NextMove?.Id);
            PowerCmd.Apply<StrengthPower>(cs, slug, 1m, slug, null);
            MonsterLifecycle.TakeTurn(cs, slug);
            hp -= 2 * (3 + 1);
            Assert.Equal(hp, player.CurrentHp, "WHIP_SLAP is two hits, each carrying Strength");
            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            MonsterLifecycle.TakeTurn(cs, slug);
            hp -= glomp + 1;
            Assert.Equal(hp, player.CurrentHp, "GLOMP with Strength");
            MonsterLifecycle.PrepareForNextTurn(cs, slug);
            MonsterLifecycle.TakeTurn(cs, slug);
            Assert.Equal(4, player.GetPowerAmount<FrailPower>(), "a second GOOP stacks Frail");
            Assert.Equal(1, player.Powers.Count);
        }
    }

    public static void Test_CorpseSlugsSeapunk_SeapunkMovesDamageBlockAndSelfStrength()
    {
        foreach (int asc in new[] { 0, 8, 9 })
        {
            var cs = NewCombat(asc, 67UL);
            var punk = SpawnEncounter(cs, Punk)[0];
            var player = cs.Player;
            int kick = asc >= 9 ? 13 : 11;
            int block = asc >= 8 ? 8 : 7;
            int strength = asc >= 9 ? 2 : 1;
            MonsterLifecycle.AfterCreatureAdded(cs, punk);
            Assert.Equal(0, punk.Powers.Count, "Seapunk.AfterAddedToRoom only runs the no-op base");
            Assert.Equal(SeaKick, punk.Ai.NextMove?.Id);
            MonsterLifecycle.OnSideSwitch(punk);

            MonsterLifecycle.TakeTurn(cs, punk);
            int hp = PlayerHp - kick;
            Assert.Equal(hp, player.CurrentHp, $"SEA_KICK A{asc}");
            MonsterLifecycle.PrepareForNextTurn(cs, punk);
            Assert.Equal(Spin, punk.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, punk);
            hp -= 4 * 2;
            Assert.Equal(hp, player.CurrentHp, "SPINNING_KICK 2x4");
            MonsterLifecycle.PrepareForNextTurn(cs, punk);
            Assert.Equal(Burp, punk.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, punk);
            Assert.Equal(hp, player.CurrentHp, "BUBBLE_BURP deals no damage");
            Assert.Equal(block, punk.Block, $"BUBBLE_BURP block A{asc}");
            var str = Assert.NotNull(punk.GetPower<StrengthPower>());
            Assert.Equal(strength, str.Amount, $"BUBBLE_BURP Strength A{asc}");
            Assert.Equal(punk.CombatId, str.ApplierId);
            Assert.Null(player.GetPower<StrengthPower>(), "BUBBLE_BURP buffs the Seapunk itself, never the player");
            Assert.Equal(0, player.Powers.Count);

            MonsterLifecycle.PrepareForNextTurn(cs, punk);
            MonsterLifecycle.TakeTurn(cs, punk);
            hp -= kick + strength;
            Assert.Equal(hp, player.CurrentHp, "SEA_KICK with Strength");
            MonsterLifecycle.PrepareForNextTurn(cs, punk);
            MonsterLifecycle.TakeTurn(cs, punk);
            hp -= 4 * (2 + strength);
            Assert.Equal(hp, player.CurrentHp, "every SPINNING_KICK hit carries Strength");
            MonsterLifecycle.PrepareForNextTurn(cs, punk);
            MonsterLifecycle.TakeTurn(cs, punk);
            Assert.Equal(2 * strength, punk.GetPowerAmount<StrengthPower>(), "Strength stacks");
            Assert.Equal(2 * block, punk.Block, "block stacks while no turn start clears it");
        }
    }

    public static void Test_CorpseSlugsSeapunk_RavenousStunsAndStrengthensTheSurvivor()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            int ravenous = asc >= 9 ? 5 : 4;
            int glomp = asc >= 9 ? 9 : 8;
            for (int starter = 0; starter < 3; starter++)
            {
                var cs = NewCombat(asc, 71UL);
                var a = SpawnSlug(cs, starter);
                var b = SpawnSlug(cs, (starter + 1) % 3);
                MonsterLifecycle.AfterCreatureAdded(cs, a);
                MonsterLifecycle.AfterCreatureAdded(cs, b);
                string pending = SlugCycle[(starter + 1) % 3];
                Assert.Equal(pending, b.Ai.NextMove?.Id);
                int aiDraws = cs.Rng.MonsterAi.Counter;

                CreatureCmd.Damage(cs, a, 999m, ValueProp.Move, cs.Player, null, null);
                Assert.True(a.IsDead);
                Assert.False(cs.Monsters.Contains(a), "the dead slug leaves the combat");
                Assert.Equal(0, a.Powers.Count, "the dead slug's powers are removed");
                Assert.True(b.IsStunned, $"A{asc} starter {starter}");
                var stun = b.Ai.NextMove!;
                Assert.Equal(Stunned, stun.Id);
                Assert.Equal(pending, stun.FollowUpStateId, "the stun resumes the move it replaced");
                Assert.True(stun.MustPerformOnceBeforeTransitioning);
                Assert.Equal(IntentType.Stun, stun.Intents[0].IntentType);
                Assert.Equal(Stunned, b.Ai.CurrentStateId);
                Assert.True(CorpseSlug.GetExtra(b).IsRavenous);
                var str = Assert.NotNull(b.GetPower<StrengthPower>());
                Assert.Equal(ravenous, str.Amount, $"Ravenous Strength A{asc}");
                Assert.Equal(b.CombatId, str.ApplierId);
                Assert.Equal(ravenous, b.GetPowerAmount<RavenousPower>(), "Ravenous is not consumed");

                int hp = cs.Player.CurrentHp;
                MonsterLifecycle.PrepareForNextTurn(cs, b);
                Assert.Equal(Stunned, b.Ai.NextMove?.Id, "an unperformed stun survives the re-roll");
                MonsterLifecycle.OnSideSwitch(b);
                MonsterLifecycle.TakeTurn(cs, b);
                Assert.Equal(hp, cs.Player.CurrentHp, "the stunned turn does nothing to the player");
                Assert.False(CorpseSlug.GetExtra(b).IsRavenous, "the stunned move ends the devouring");
                MonsterLifecycle.PrepareForNextTurn(cs, b);
                Assert.Equal(pending, b.Ai.NextMove?.Id);
                Assert.SequenceEqual(new[] { pending, pending }, b.Ai.StateLog);
                MonsterLifecycle.TakeTurn(cs, b);
                int expected = pending == Whip ? 2 * (3 + ravenous) : pending == Glomp ? glomp + ravenous : 0;
                Assert.Equal(hp - expected, cs.Player.CurrentHp, $"{pending} after the stun A{asc}");
                Assert.Equal(aiDraws, cs.Rng.MonsterAi.Counter);
            }
        }
    }

    public static void Test_CorpseSlugsSeapunk_RavenousIgnoresSelfOtherSidePreventedAndDeadOwner()
    {
        var cs = NewCombat(0, 73UL);
        var a = SpawnSlug(cs, 0);
        var b = SpawnSlug(cs, 1);
        MonsterLifecycle.AfterCreatureAdded(cs, a);
        MonsterLifecycle.AfterCreatureAdded(cs, b);
        var power = Assert.NotNull(b.GetPower<RavenousPower>());
        ulong before = StateHasher.Hash(cs);

        power.AfterDeath(cs, b, false);
        power.AfterDeath(cs, cs.Player, false);
        power.AfterDeath(cs, a, true);
        Assert.Equal(before, StateHasher.Hash(cs), "own death, a death on the other side and a prevented removal do nothing");
        Assert.False(b.IsStunned);

        b.CurrentHp = 0;
        power.AfterDeath(cs, a, false);
        Assert.False(b.IsStunned, "a dead owner does nothing");
        Assert.Null(b.GetPower<StrengthPower>());
        Assert.False(CorpseSlug.GetExtra(b).IsRavenous);

        b.CurrentHp = b.MaxHp;
        power.AfterDeath(cs, a, false);
        Assert.True(b.IsStunned, "a living owner reacts to a teammate's death");
        Assert.Equal(4, b.GetPowerAmount<StrengthPower>());
        Assert.True(CorpseSlug.GetExtra(b).IsRavenous);
    }

    public static void Test_CorpseSlugsSeapunk_SimultaneousDeathsTriggerNoRavenous()
    {
        var cs = NewCombat(0, 79UL);
        var a = SpawnSlug(cs, 0);
        var b = SpawnSlug(cs, 1);
        MonsterLifecycle.AfterCreatureAdded(cs, a);
        MonsterLifecycle.AfterCreatureAdded(cs, b);
        CreatureCmd.Damage(cs, new CreatureState[] { a, b }, 999m, ValueProp.Move, cs.Player, null, null);
        Assert.True(a.IsDead && b.IsDead);
        Assert.Equal(0, cs.Monsters.Count);
        Assert.False(b.IsStunned, "the second slug is already dead when the first death is processed");
        Assert.False(CorpseSlug.GetExtra(a).IsRavenous);
        Assert.False(CorpseSlug.GetExtra(b).IsRavenous);
        Assert.Equal(Glomp, b.Ai.NextMove?.Id);
    }

    public static void Test_CorpseSlugsSeapunk_RavenousAfterTheSurvivorActedRepeatsItsLastMove()
    {
        var cs = NewCombat(0, 83UL);
        var a = SpawnSlug(cs, 2);
        var b = SpawnSlug(cs, 0);
        MonsterLifecycle.AfterCreatureAdded(cs, a);
        MonsterLifecycle.AfterCreatureAdded(cs, b);
        MonsterLifecycle.OnSideSwitch(a);
        MonsterLifecycle.OnSideSwitch(b);
        MonsterLifecycle.TakeTurn(cs, a);
        MonsterLifecycle.TakeTurn(cs, b);
        Assert.Equal(PlayerHp - 6, cs.Player.CurrentHp);

        CreatureCmd.Kill(cs, a);
        Assert.Equal(Stunned, b.Ai.NextMove?.Id);
        Assert.Equal(Whip, b.Ai.NextMove!.FollowUpStateId, "the follow-up is the last logged move, which b has just performed");
        MonsterLifecycle.PrepareForNextTurn(cs, b);
        Assert.Equal(Stunned, b.Ai.NextMove?.Id);
        MonsterLifecycle.TakeTurn(cs, b);
        Assert.Equal(PlayerHp - 6, cs.Player.CurrentHp);
        MonsterLifecycle.PrepareForNextTurn(cs, b);
        Assert.Equal(Whip, b.Ai.NextMove?.Id, "WHIP_SLAP repeats after the stun");
        MonsterLifecycle.TakeTurn(cs, b);
        Assert.Equal(PlayerHp - 6 - 2 * (3 + 4), cs.Player.CurrentHp);
        MonsterLifecycle.PrepareForNextTurn(cs, b);
        Assert.Equal(Glomp, b.Ai.NextMove?.Id);
        Assert.SequenceEqual(new[] { Whip, Whip, Glomp }, b.Ai.StateLog);
    }

    public static void Test_CorpseSlugsSeapunk_ExtraStateCloneAndCanonical()
    {
        var extra = new CorpseSlugExtraState { StarterMoveIdx = 2 };
        var clone = (CorpseSlugExtraState)extra.DeepClone();
        Assert.Equal(2, clone.StarterMoveIdx);
        Assert.False(clone.IsRavenous);
        Assert.Equal(StateHasher.Hash(extra), StateHasher.Hash(clone));
        clone.IsRavenous = true;
        Assert.False(extra.IsRavenous);
        Assert.NotEqual(StateHasher.Hash(extra), StateHasher.Hash(clone));
        Assert.NotEqual(StateHasher.Hash(extra), StateHasher.Hash(new CorpseSlugExtraState { StarterMoveIdx = 1 }));
    }

    public static void Test_CorpseSlugsSeapunk_StunnedStateClonesIndependently()
    {
        var cs = NewCombat(0, 89UL);
        var a = SpawnSlug(cs, 0);
        var b = SpawnSlug(cs, 1);
        MonsterLifecycle.AfterCreatureAdded(cs, a);
        MonsterLifecycle.AfterCreatureAdded(cs, b);
        CreatureCmd.Damage(cs, a, 999m, ValueProp.Move, cs.Player, null, null);
        Assert.True(b.IsStunned);

        var clone = cs.DeepClone();
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        Assert.Equal(1, clone.Monsters.Count);
        var cb = clone.Monsters[0];
        Assert.True(cb.IsStunned);
        Assert.True(CorpseSlug.GetExtra(cb).IsRavenous);
        Assert.False(ReferenceEquals(cb.Extra, b.Extra));
        var clonedPower = Assert.NotNull(cb.GetPower<RavenousPower>());
        Assert.False(ReferenceEquals(b.GetPower<RavenousPower>(), clonedPower));
        Assert.True(ReferenceEquals(cb, clonedPower.Owner));

        MonsterLifecycle.OnSideSwitch(cb);
        MonsterLifecycle.TakeTurn(clone, cb);
        Assert.False(CorpseSlug.GetExtra(cb).IsRavenous);
        Assert.True(CorpseSlug.GetExtra(b).IsRavenous, "the source is unchanged");
        Assert.True(b.IsStunned);
        Assert.NotEqual(StateHasher.Hash(cs), StateHasher.Hash(clone));
    }

    public static void Test_CorpseSlugsSeapunk_CombatSimSeapunkWeakEndToEnd()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, Punk, asc);
            var cs = sim.State;
            Assert.Equal(1, cs.Monsters.Count);
            var punk = cs.Monsters[0];
            Assert.Equal(Seapunk.MonsterIdEntry, punk.MonsterId);
            int min = asc >= 8 ? 47 : 44;
            Assert.True(punk.MaxHp >= min && punk.MaxHp <= min + 2, $"hp {punk.MaxHp}");
            int kick = asc >= 9 ? 13 : 11;
            int strength = asc >= 9 ? 2 : 1;
            int block = asc >= 8 ? 8 : 7;
            var moves = new[] { SeaKick, Spin, Burp, SeaKick, Spin };
            var damage = new[] { kick, 8, 0, kick + strength, 4 * (2 + strength) };
            int hp = cs.Player.CurrentHp;
            for (int t = 0; t < moves.Length; t++)
            {
                Assert.Equal(moves[t], punk.Ai.NextMove?.Id, $"A{asc} turn {t + 1}");
                sim.Step(GameAction.EndTurn);
                hp -= damage[t];
                Assert.Equal(hp, cs.Player.CurrentHp, $"A{asc} after enemy turn {t + 1}");
                if (t == 2)
                {
                    Assert.Equal(block, punk.Block, "BUBBLE_BURP block lasts through the player's turn");
                    Assert.Equal(strength, punk.GetPowerAmount<StrengthPower>());
                    Assert.Null(cs.Player.GetPower<StrengthPower>());
                }
                if (t == 3)
                {
                    Assert.Equal(0, punk.Block, "the block is cleared when the enemy turn starts");
                }
            }
            Assert.Equal(0, cs.Rng.MonsterAi.Counter);
        }
    }

    public static void Test_CorpseSlugsSeapunk_CombatSimCorpseSlugsWeakEndToEnd()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            const ulong seed = 7UL;
            var sim = CombatSim.Create(seed, StartingDecks.Ironclad, Slugs, asc);
            var cs = sim.State;
            int k = CombatRunContext.Default.CreateEncounterRng(seed, Slugs).NextInt(3);
            int glomp = asc >= 9 ? 9 : 8;
            int hp = cs.Player.CurrentHp;
            for (int t = 0; t < 6; t++)
            {
                string m1 = SlugCycle[(k + t) % 3];
                string m2 = SlugCycle[(k + 1 + t) % 3];
                Assert.Equal(m1, cs.Monsters[0].Ai.NextMove?.Id, $"A{asc} turn {t + 1}");
                Assert.Equal(m2, cs.Monsters[1].Ai.NextMove?.Id, $"A{asc} turn {t + 1}");
                sim.Step(GameAction.EndTurn);
                hp -= SlugDamage(m1, glomp) + SlugDamage(m2, glomp);
                Assert.Equal(hp, cs.Player.CurrentHp, $"A{asc} after enemy turn {t + 1}");
                if (m1 == Goop || m2 == Goop)
                {
                    Assert.True(cs.Player.GetPowerAmount<FrailPower>() > 0, "GOOP leaves the player Frail");
                }
            }
            Assert.Equal(PlayerHp - 4 * (6 + glomp), cs.Player.CurrentHp, "two full cycles of both slugs");
            Assert.Equal(0, cs.Rng.MonsterAi.Counter);
        }
    }

    public static void Test_CorpseSlugsSeapunk_CombatSimStrikeKillTriggersRavenous()
    {
        var deck = Enumerable.Repeat(StrikeIronclad.CardId, 10).ToArray();
        var pendingSeen = new HashSet<string>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var sim = CombatSim.Create(seed, deck, Slugs, 0);
            var cs = sim.State;
            var a = cs.Monsters[0];
            var b = cs.Monsters[1];
            string pending = b.Ai.NextMove!.Id;
            pendingSeen.Add(pending);
            a.CurrentHp = 1;
            sim.Step(GameAction.Play(0, 0));
            Assert.True(a.IsDead, $"seed {seed}");
            Assert.Equal(1, cs.Monsters.Count);
            Assert.True(b.IsStunned, $"seed {seed}");
            Assert.Equal(4, b.GetPowerAmount<StrengthPower>());
            Assert.Equal(CombatOutcome.InProgress, sim.Outcome);

            int hp = cs.Player.CurrentHp;
            sim.Step(GameAction.EndTurn);
            Assert.Equal(hp, cs.Player.CurrentHp, "the stunned survivor does not act");
            Assert.False(CorpseSlug.GetExtra(b).IsRavenous);
            Assert.Equal(pending, b.Ai.NextMove?.Id);
            sim.Step(GameAction.EndTurn);
            int expected = pending == Whip ? 2 * (3 + 4) : pending == Glomp ? 8 + 4 : 0;
            Assert.Equal(hp - expected, cs.Player.CurrentHp, $"seed {seed} {pending} with Ravenous Strength");
        }
        Assert.Equal(3, pendingSeen.Count, "every pending move was stunned at least once");
    }

    private static int SlugDamage(string move, int glomp) => move == Whip ? 6 : move == Glomp ? glomp : 0;

    private static CombatState NewCombat(int ascension, ulong seed) => NewCombat(ascension, new RunRngSet(new Rng(seed)));

    private static CombatState NewCombat(int ascension, RunRngSet rng)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, PlayerHp, PlayerHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, Slugs, ascension, rng);
        cs.NextCreatureId = 1;
        cs.CurrentSide = CombatSide.Player;
        cs.IsInProgress = true;
        return cs;
    }

    private static List<MonsterState> SpawnEncounter(CombatState cs, string encounterId)
    {
        var spawned = new List<MonsterState>();
        MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(encounterId), CombatRunContext.Default, spawned);
        foreach (var monster in spawned)
        {
            MonsterLifecycle.SetUpForCombat(cs, monster);
        }
        return spawned;
    }

    private static MonsterState SpawnAndAdd(CombatState cs, MonsterSpawn spawn)
    {
        var monster = MonsterSpawner.Spawn(cs, spawn);
        cs.Monsters.Add(monster);
        MonsterLifecycle.SetUpForCombat(cs, monster);
        return monster;
    }

    private static MonsterState SpawnSlug(CombatState cs, int starterMoveIdx)
        => SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(CorpseSlug.MonsterIdEntry), null, m => CorpseSlug.GetExtra(m).StarterMoveIdx = starterMoveIdx));

    private static void SimulatePerformed(MonsterAiRuntime ai, MoveStateDef move)
    {
        ai.MarkPerformedAtLeastOnce(move);
        ai.OnMovePerformed(move);
    }
}
