using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public sealed record ContentSoakOptions
{
    public bool Encounters { get; init; } = true;

    public bool Cards { get; init; } = true;

    public bool Relics { get; init; } = true;

    public bool Potions { get; init; } = true;

    public Func<string, bool>? EncounterFilter { get; init; }

    public Func<string, bool>? CardFilter { get; init; }

    public Func<string, bool>? RelicFilter { get; init; }

    public Func<string, bool>? PotionFilter { get; init; }

    public int EncounterSeeds { get; init; } = 3;

    public int CardSeeds { get; init; } = 2;

    public int RelicSeeds { get; init; } = 2;

    public int PotionSeeds { get; init; } = 2;

    public int EncounterStepCap { get; init; } = 300;

    public int CardStepCap { get; init; } = 150;

    public int ModelStepCap { get; init; } = 150;

    public int RandomDeckSize { get; init; } = 12;

    public int CardCopies { get; init; } = 6;

    public int CardStrikes { get; init; } = 4;

    public string WeakEncounterId { get; init; } = ContentSoak.DefaultWeakEncounterId;
}

public sealed class ContentSoakReport
{
    public List<string> Failures { get; } = new();

    public int Cases { get; set; }

    public int Steps { get; set; }

    public int Capped { get; set; }

    public int Finished { get; set; }

    public string Summary => $"{Cases} cases, {Steps} steps, {Finished} finished, {Capped} capped, {Failures.Count} failures";

    public void AssertClean(int maxListed = ContentSoak.MaxListedFailures)
    {
        if (Failures.Count == 0)
        {
            return;
        }
        var listed = Failures.Take(maxListed).ToList();
        var more = Failures.Count > listed.Count ? $"{Environment.NewLine}  ... and {Failures.Count - listed.Count} more" : string.Empty;
        Assert.Fail($"content soak: {Failures.Count} failures of {Cases} cases{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", listed)}{more}");
    }
}

public static class ContentSoak
{
    public const string DefaultWeakEncounterId = "NIBBITS_WEAK";

    public const int MaxListedFailures = 30;

    private static readonly ulong[] SeedBases = { 1UL, 7919UL, 104729UL };

    public static ContentSoakReport Run(ContentSoakOptions? options = null)
    {
        options ??= new ContentSoakOptions();
        var report = new ContentSoakReport();
        if (options.Encounters)
        {
            RunEncounters(options, report);
        }
        if (options.Cards)
        {
            RunCards(options, report);
        }
        if (options.Relics)
        {
            RunRelics(options, report);
        }
        if (options.Potions)
        {
            RunPotions(options, report);
        }
        return report;
    }

    public static void RunEncounters(ContentSoakOptions options, ContentSoakReport report)
    {
        var pool = RandomDeckPool(options);
        foreach (var id in EncounterRegistry.AllIds)
        {
            var def = EncounterRegistry.Get(id);
            if (IsExcluded(def.GetType()) || !(options.EncounterFilter?.Invoke(id) ?? true))
            {
                continue;
            }
            for (int s = 0; s < options.EncounterSeeds; s++)
            {
                ulong seed = CaseSeed(id, s);
                var random = new System.Random(unchecked((int)seed));
                var deck = RandomDeck(pool, options.RandomDeckSize, random);
                RunCase(report, $"{id}/{seed}", random, options.EncounterStepCap, () => CombatSim.Create(new RunRngSet(seed), deck, id, 0, null, null), null);
            }
        }
    }

    public static void RunCards(ContentSoakOptions options, ContentSoakReport report)
    {
        foreach (var id in CardRegistry.AllIds)
        {
            var def = CardRegistry.Get(id);
            if (IsExcluded(def.GetType()) || !(options.CardFilter?.Invoke(id) ?? true))
            {
                continue;
            }
            var deck = new List<CardSpec>(options.CardCopies + options.CardStrikes);
            for (int i = 0; i < options.CardCopies; i++)
            {
                deck.Add(new CardSpec(id, false));
            }
            for (int i = 0; i < options.CardStrikes; i++)
            {
                deck.Add(new CardSpec(StrikeIronclad.CardId, false));
            }
            for (int s = 0; s < options.CardSeeds; s++)
            {
                ulong seed = CaseSeed(id, s);
                var random = new System.Random(unchecked((int)seed));
                RunCase(report, $"{id}/{seed}", random, options.CardStepCap, () => CombatSim.Create(new RunRngSet(seed), deck, options.WeakEncounterId, 0, null, null), null);
            }
        }
    }

    public static void RunRelics(ContentSoakOptions options, ContentSoakReport report)
    {
        var pool = RandomDeckPool(options);
        foreach (var id in RelicRegistry.AllIds)
        {
            if (IsExcluded(RelicRegistry.TypeOf(id)) || !(options.RelicFilter?.Invoke(id) ?? true))
            {
                continue;
            }
            for (int s = 0; s < options.RelicSeeds; s++)
            {
                ulong seed = CaseSeed(id, s);
                var random = new System.Random(unchecked((int)seed));
                var deck = RandomDeck(pool, options.RandomDeckSize, random);
                RunCase(report, $"{id}/{seed}", random, options.ModelStepCap, () => CombatSim.Create(new RunRngSet(seed), deck, options.WeakEncounterId, 0, null, null),
                    sim => RelicCmd.Obtain(sim.State.Run, RelicRegistry.Create(id), sim.State.Player));
            }
        }
    }

    public static void RunPotions(ContentSoakOptions options, ContentSoakReport report)
    {
        var pool = RandomDeckPool(options);
        foreach (var id in PotionRegistry.AllIds)
        {
            if (IsExcluded(PotionRegistry.TypeOf(id)) || !(options.PotionFilter?.Invoke(id) ?? true))
            {
                continue;
            }
            for (int s = 0; s < options.PotionSeeds; s++)
            {
                ulong seed = CaseSeed(id, s);
                var random = new System.Random(unchecked((int)seed));
                var deck = RandomDeck(pool, options.RandomDeckSize, random);
                RunCase(report, $"{id}/{seed}", random, options.ModelStepCap, () => CombatSim.Create(new RunRngSet(seed), deck, options.WeakEncounterId, 0, null, null),
                    sim =>
                    {
                        var result = PotionCmd.TryToProcure(sim.State.Run, PotionRegistry.Create(id), sim.State.Player, 0);
                        if (!result.Success)
                        {
                            throw new InvalidOperationException($"potion {id} could not be procured: {result.FailureReason}");
                        }
                    });
            }
        }
    }

    private static void RunCase(ContentSoakReport report, string label, System.Random random, int stepCap, Func<CombatSim> create, Action<CombatSim>? equip)
    {
        report.Cases++;
        var legal = new List<GameAction>(32);
        int steps = 0;
        try
        {
            var sim = create();
            equip?.Invoke(sim);
            while (!sim.IsTerminal && steps < stepCap)
            {
                sim.LegalActions(legal);
                if (legal.Count == 0)
                {
                    throw new InvalidOperationException("a live combat has no legal action");
                }
                sim.Step(legal[random.Next(legal.Count)]);
                steps++;
            }
            if (sim.IsTerminal)
            {
                report.Finished++;
            }
            else
            {
                report.Capped++;
            }
        }
        catch (Exception ex)
        {
            report.Failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
        }
        report.Steps += steps;
    }

    private static List<CardSpec> RandomDeckPool(ContentSoakOptions options)
    {
        var pool = new List<CardSpec>();
        foreach (var id in CardRegistry.AllIds)
        {
            var def = CardRegistry.Get(id);
            if (def.Rarity == CardRarity.Curse || def.Type == CardType.Curse || IsExcluded(def.GetType()) || !(options.CardFilter?.Invoke(id) ?? true))
            {
                continue;
            }
            pool.Add(new CardSpec(id, false));
        }
        if (!pool.Exists(c => c.Id == StrikeIronclad.CardId))
        {
            pool.Add(new CardSpec(StrikeIronclad.CardId, false));
        }
        if (!pool.Exists(c => c.Id == DefendIronclad.CardId))
        {
            pool.Add(new CardSpec(DefendIronclad.CardId, false));
        }
        return pool;
    }

    private static List<CardSpec> RandomDeck(List<CardSpec> pool, int size, System.Random random)
    {
        var deck = new List<CardSpec>(size);
        for (int i = 0; i < size; i++)
        {
            deck.Add(pool[random.Next(pool.Count)]);
        }
        return deck;
    }

    private static bool IsExcluded(Type type) => type.IsDefined(typeof(SimExcludeAttribute), false);

    private static ulong CaseSeed(string id, int index)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in id)
        {
            hash = unchecked((hash ^ c) * 1099511628211UL);
        }
        return unchecked(SeedBases[index % SeedBases.Length] + (ulong)index * 0x9E3779B97F4A7C15UL + (hash & 0xFFFFUL)) & 0x7FFFFFFFUL;
    }
}

public static class ContentSoakTests
{
    public static void Test_ContentSoak_EveryEncounter()
    {
        var report = new ContentSoakReport();
        ContentSoak.RunEncounters(new ContentSoakOptions(), report);
        Console.WriteLine($"     ContentSoak encounters: {report.Summary}");
        Assert.True(report.Cases >= EncounterRegistry.AllIds.Count, "every registered encounter runs");
        report.AssertClean();
    }

    public static void Test_ContentSoak_EveryCard()
    {
        var report = new ContentSoakReport();
        ContentSoak.RunCards(new ContentSoakOptions(), report);
        Console.WriteLine($"     ContentSoak cards: {report.Summary}");
        Assert.True(report.Cases >= CardRegistry.AllIds.Count, "every registered card runs");
        report.AssertClean();
    }

    public static void Test_ContentSoak_EveryRelic()
    {
        var report = new ContentSoakReport();
        ContentSoak.RunRelics(new ContentSoakOptions(), report);
        Console.WriteLine($"     ContentSoak relics (obtained after combat start; BeforeCombatStart and turn-1 start hooks not covered): {report.Summary}");
        report.AssertClean();
    }

    public static void Test_ContentSoak_EveryPotion()
    {
        var report = new ContentSoakReport();
        ContentSoak.RunPotions(new ContentSoakOptions(), report);
        Console.WriteLine($"     ContentSoak potions (procured into slot 0 after combat start; out-of-combat use not covered until W2 R6): {report.Summary}");
        report.AssertClean();
    }

    public static void Test_ContentSoak_HelperFiltersAndListsFailures()
    {
        var report = ContentSoak.Run(new ContentSoakOptions
        {
            Cards = false,
            Relics = false,
            Potions = false,
            EncounterSeeds = 1,
            EncounterFilter = id => id == ContentSoak.DefaultWeakEncounterId
        });
        Assert.Equal(1, report.Cases, "the filter keeps one encounter with one seed");
        var failing = new ContentSoakReport { Cases = 1 };
        failing.Failures.Add("X/1: InvalidOperationException: boom");
        var thrown = Assert.Throws<AssertionException>(() => failing.AssertClean());
        Assert.True(thrown.Message.Contains("X/1: InvalidOperationException: boom", StringComparison.Ordinal), "the failure line is listed");
    }
}
