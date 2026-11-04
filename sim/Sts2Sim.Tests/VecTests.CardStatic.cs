using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Registry;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// The static card table (schema append 2026-11-04, <c>CardStatic</c>): published in the schema JSON, one row per cards
/// vocab id; checked against the game's own card constructors and OnUpgrade bodies as the committed knowledge export
/// records them (knowledge/source/v0.111.0/cards.jsonl: <c>source.base_constructor</c>, <c>source.upgrade_source</c>).
/// </summary>
public static unsafe partial class VecTests
{
    private static string KnowledgeCards([CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "knowledge", "source", "v0.111.0", "cards.jsonl"));

    private static int Sum(Regex re, string text) => re.Matches(text).Sum(m => int.Parse(m.Groups[1].Value));

    public static void Test_Vec_CardStaticTable()
    {
        using var doc = JsonDocument.Parse(Schema.ToJson());
        var cs = doc.RootElement.GetProperty("card_static");
        Assert.SequenceEqual(CardStatic.Fields, cs.GetProperty("fields").EnumerateArray().Select(e => e.GetString()!));
        var rows = cs.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().Select(e => e.GetInt32()).ToArray()).ToList();
        Assert.Equal(Vocab.Cards.Count, rows.Count, "one row per cards vocab id");
        Assert.True(rows.All(r => r.Length == CardStatic.Fields.Length));
        Assert.True(rows[0].All(v => v == 0) && rows[1].All(v => v == 0), "rows 0 (none) and 1 (unknown) are zero");
        for (int i = 0; i < rows.Count; i++)
        {
            Assert.SequenceEqual(CardStatic.Rows[i], rows[i], $"row {i}");
        }
        int C(string f) => CardStatic.Col(f);
        int[] Row(string id) => rows[Vocab.Card(id)];

        // spot checks (the sim's defs; the game's: Models.Cards/StrikeIronclad.cs, Bash.cs:146-150)
        var strike = Row("STRIKE_IRONCLAD");
        Assert.Equal(1, strike[C("ported")]);
        Assert.Equal(1, strike[C("cost")]);
        Assert.Equal((int)CardType.Attack, strike[C("type")]);
        Assert.Equal((int)CardRarity.Basic, strike[C("rarity")]);
        Assert.Equal((int)TargetType.AnyEnemy, strike[C("target")]);
        Assert.Equal(6, strike[C("damage")]);
        Assert.Equal(9, strike[C("damage_upgraded")]);
        Assert.Equal(1, strike[C("hits")]);
        var bash = Row("BASH");
        Assert.Equal(Vocab.CardVar("VulnerablePower"), bash[C("var_id_1")]);
        Assert.Equal(2, bash[C("var_val_1")]);
        Assert.Equal(3, bash[C("var_val_upgraded_1")]);
        Assert.Equal(10, bash[C("damage_upgraded")]);
        Assert.Equal(5, Row("DEFEND_IRONCLAD")[C("block")]);
        Assert.Equal(8, Row("DEFEND_IRONCLAD")[C("block_upgraded")]);
        Assert.Equal(HC.CostX, Row("CASCADE")[C("cost")], "X cost");
        Assert.Equal(HC.CostUnplayable, Row("ASCENDERS_BANE")[C("cost")], "unplayable curse");
        int ported = rows.Count(r => r[C("ported")] == 1);
        Assert.Equal(CardRegistry.AllIds.Count(id => Vocab.Card(id) > VocabList.Unknown), ported, "every registry card is ported");
        // every card id has its catalog / def type, except MAD_SCIENCE: its type is the Tinker Time choice, None on a fresh
        // card (game Models.Cards/MadScience.cs:814 Type => TinkerTimeType; sim GetCardType)
        Assert.SequenceEqual(new[] { "MAD_SCIENCE" }, Enumerable.Range(2, rows.Count - 2).Where(i => rows[i][C("type")] == 0).Select(i => Vocab.Cards.Names[i]));
        Assert.Equal(ported, rows.Count(r => r[C("ported")] == 1 && r[C("upgrade_known")] == 1), "every ported card's upgrade ran on a probe");

        // against the game's constructors and OnUpgrade bodies (knowledge export of v0.111.0)
        string path = KnowledgeCards();
        Assert.True(File.Exists(path), path);
        var ctor = new Regex(@"^(-?\d+),\s*CardType\.(\w+),\s*CardRarity\.(\w+),\s*TargetType\.(\w+)");
        var dmgUp = new Regex(@"DynamicVars\.Damage\.UpgradeValueBy\((-?\d+)m\)");
        var blkUp = new Regex(@"DynamicVars\.Block\.UpgradeValueBy\((-?\d+)m\)");
        var costUp = new Regex(@"EnergyCost\.UpgradeBy\((-?\d+)\)");
        int ctors = 0, damages = 0, blocks = 0, costs = 0;
        foreach (string line in File.ReadLines(path))
        {
            using var k = JsonDocument.Parse(line);
            var root = k.RootElement;
            string id = root.GetProperty("id").GetString()!;
            int vid = Vocab.Cards.Index(id);
            if (vid <= VocabList.Unknown || !root.TryGetProperty("source", out var src) || src.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var row = rows[vid];
            // MAD_SCIENCE's type and target are its Tinker Time choice (game Models.Cards/MadScience.cs:814), not the constructor's
            if (id != "MAD_SCIENCE" && src.TryGetProperty("base_constructor", out var bc) && bc.GetString() is { } bcs && ctor.Match(bcs) is { Success: true } m)
            {
                int cost = int.Parse(m.Groups[1].Value);
                if (row[C("cost")] == HC.CostX)
                {
                    // an X card's constructor number is not its cost (HasEnergyCostX overrides it, e.g. DIRGE)
                }
                else if (cost < 0)
                {
                    Assert.Equal(HC.CostUnplayable, row[C("cost")], $"{id}: game cost {cost}");
                }
                else
                {
                    Assert.Equal(cost, row[C("cost")], $"{id} cost");
                }
                Assert.Equal((int)Enum.Parse<CardType>(m.Groups[2].Value), row[C("type")], $"{id} type");
                Assert.Equal((int)Enum.Parse<CardRarity>(m.Groups[3].Value), row[C("rarity")], $"{id} rarity");
                Assert.Equal((int)Enum.Parse<TargetType>(m.Groups[4].Value), row[C("target")], $"{id} target");
                ctors++;
            }
            if (row[C("ported")] != 1 || !src.TryGetProperty("upgrade_source", out var us) || us.GetString() is not { } up)
            {
                continue;
            }
            if (dmgUp.IsMatch(up) && Enumerable.Range(0, 4).Any(j => row[C("var_id_0") + j] == Vocab.CardVar(DynamicVarSet.DamageName)))
            {
                Assert.Equal(Sum(dmgUp, up), row[C("damage_upgraded")] - row[C("damage")], $"{id} damage upgrade");
                damages++;
            }
            if (blkUp.IsMatch(up) && Enumerable.Range(0, 4).Any(j => row[C("var_id_0") + j] == Vocab.CardVar(DynamicVarSet.BlockName)))
            {
                Assert.Equal(Sum(blkUp, up), row[C("block_upgraded")] - row[C("block")], $"{id} block upgrade");
                blocks++;
            }
            if (costUp.IsMatch(up) && row[C("cost")] >= 0)
            {
                Assert.Equal(Math.Max(0, row[C("cost")] + Sum(costUp, up)), row[C("cost_upgraded")], $"{id} cost upgrade");
                costs++;
            }
        }
        Assert.True(ctors > 550 && damages > 100 && blocks > 50 && costs > 40, $"checked {ctors} constructors, {damages} damage / {blocks} block / {costs} cost upgrades");
        Console.WriteLine($"     card_static: {rows.Count} rows ({ported} ported); game constructors {ctors}, damage upgrades {damages}, block upgrades {blocks}, cost upgrades {costs} agree");
    }
}
