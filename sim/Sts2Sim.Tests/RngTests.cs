using Sts2Sim.Core;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class RngTests
{
    private const string OracleSeed = "Q3X6PF8S37GA";

    private const ulong OracleSeedHash = 0x7C7021C5035C8FADUL;

    private static readonly (RunRngType Type, int Counter, ulong S0, ulong S1, ulong S2, ulong S3)[] OracleRunStreams =
    {
        (RunRngType.UpFront, 418, 0x8FBCB72ECD37F616UL, 0x1F3420C9ECA07212UL, 0xDC937501F403DA82UL, 0x9A6B88FB40E57F5AUL),
        (RunRngType.Shuffle, 282, 0x281ADB7FCE34EF9CUL, 0x176A100AB5403C2DUL, 0x149B8D646BF5EF7AUL, 0x99FE5F6CD6694A0FUL),
        (RunRngType.UnknownMapPoint, 2, 0xC9E9C84D766FD05EUL, 0xF1CD37B5A6BC6EC2UL, 0x617D6253B70FC701UL, 0xB6FB06BFF1E0DEADUL),
        (RunRngType.CombatCardGeneration, 0, 0x7BAD63459ECE5377UL, 0x5C407DFDC1904BEDUL, 0x5DB2D15E6F4E0699UL, 0xB1BBA6FB006DB736UL),
        (RunRngType.CombatPotionGeneration, 0, 0x524E45A8E235F94CUL, 0xFCD3E7D5C0695CA0UL, 0xFED208C86D2018F4UL, 0x325C7C9F022CD5FDUL),
        (RunRngType.CombatCardSelection, 0, 0x7EE4CF7C193E59EBUL, 0x1495BD5B98D39805UL, 0xC0ACEF5DE61123B0UL, 0x25E8C241DFE46C6CUL),
        (RunRngType.CombatEnergyCosts, 0, 0x72CC25883D2D1A60UL, 0xD9814B230A354382UL, 0x6AB6E8BA9F399BE9UL, 0xB0DFD2929A959A55UL),
        (RunRngType.CombatTargets, 0, 0xE7D7789109A08CF6UL, 0xB226DB3AD1130178UL, 0x9C4C0A27FF85AE59UL, 0x829DB1C9F5F29FAAUL),
        (RunRngType.MonsterAi, 11, 0x893D19AF34317836UL, 0xF5665BE69C425BD8UL, 0x85F45348EEF87877UL, 0x7689833A4D810178UL),
        (RunRngType.Niche, 10, 0x1B55E3D0F80D1B60UL, 0x0CE5996BFCC9250BUL, 0x2ECAAF94360C4255UL, 0x717B8D78D43F7FC1UL),
        (RunRngType.CombatOrbs, 0, 0xBC8516503C9F87D7UL, 0x158ADB371EDA8536UL, 0xCF110DC8AA659519UL, 0x4CC10F1A6FD476ADUL),
        (RunRngType.TreasureRoomRelics, 0, 0x5840F81D3EC37011UL, 0x4B7E3050588FE2D9UL, 0x4DE943426AD02210UL, 0xF7F5BC40565AD68EUL),
    };

    private static readonly (PlayerRngType Type, int Counter, ulong S0, ulong S1, ulong S2, ulong S3)[] OraclePlayerStreams =
    {
        (PlayerRngType.Rewards, 90, 0x936FB5BE376DDCD3UL, 0x9DED9F8A51D84455UL, 0xA46709B043EECC27UL, 0x330A11CE3BFE39BEUL),
        (PlayerRngType.Shops, 0, 0xD2E86AEDF4A7E08EUL, 0x1A25BA4A878C3E9BUL, 0xAF300B930E9546E7UL, 0x5E74FBB399A51A47UL),
        (PlayerRngType.Transformations, 0, 0x7E90B7B6FD4BD3BDUL, 0x10EE932EEB4B662DUL, 0x28F8DCF2ECD681E1UL, 0x405ECF8915B613B4UL),
    };

    public static void Test_Rng_Splitmix64KnownAnswers()
    {
        ulong x = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, MegaRandom.Splitmix64(ref x));
        Assert.Equal(0x9E3779B97F4A7C15UL, x);
        Assert.Equal(0x6E789E6AA1B965F4UL, MegaRandom.Splitmix64(ref x));
        Assert.Equal(0x3C6EF372FE94F82AUL, x);
        Assert.Equal(0x06C45D188009454FUL, MegaRandom.Splitmix64(ref x));
        Assert.Equal(0xDAA66D2C7DDF743FUL, x);
        Assert.Equal(0xF88BB8A8724C81ECUL, MegaRandom.Splitmix64(ref x));
        Assert.Equal(0x78DDE6E5FD29F054UL, x);
    }

    public static void Test_Rng_MegaRandomSeedingKnownAnswers()
    {
        AssertState(new MegaRandom(0UL), 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL, 0xF88BB8A8724C81ECUL);
        AssertState(new MegaRandom(0x0123456789ABCDEFUL), 0x157A3807A48FAA9DUL, 0xD573529B34A1D093UL, 0x2F90B72E996DCCBEUL, 0xA2D419334C4667ECUL);
        AssertState(new Rng(42UL).Kernel, 0xBDD732262FEB6E95UL, 0x28EFE333B266F103UL, 0x47526757130F9F52UL, 0x581CE1FF0E4AE394UL);

        var reseeded = new MegaRandom(5UL);
        reseeded.NextULong();
        reseeded.Reinitialise(0UL);
        AssertState(reseeded, 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL, 0xF88BB8A8724C81ECUL);
        reseeded.Reinitialise(1UL, 2UL, 3UL, 4UL);
        AssertState(reseeded, 1UL, 2UL, 3UL, 4UL);
        AssertState(new MegaRandom(1UL, 2UL, 3UL, 4UL), 1UL, 2UL, 3UL, 4UL);
    }

    public static void Test_Rng_MegaRandomFirstStepHandTrace()
    {
        var r = new MegaRandom(0UL);
        Assert.Equal(0x99EC5F36CB75F2B4UL, r.NextULong());
        AssertState(r, 0x74D38EFBA8E829B7UL, 0x8A9C6B4B5AADED14UL, 0xD831B65330FC88E0UL, 0xBC8312DE64D85A7EUL);
    }

    public static void Test_Rng_MegaRandomNextULongKnownAnswers()
    {
        var r = new MegaRandom(0UL);
        Assert.Equal(0x99EC5F36CB75F2B4UL, r.NextULong());
        Assert.Equal(0xBF6E1F784956452AUL, r.NextULong());
        Assert.Equal(0x1A5F849D4933E6E0UL, r.NextULong());
        Assert.Equal(0x6AA594F1262D2D2CUL, r.NextULong());
        Assert.Equal(0xBBA5AD4A1F842E59UL, r.NextULong());
        Assert.Equal(0xFFEF8375D9EBCACAUL, r.NextULong());
        AssertState(r, 0x07E3A642C4DBE605UL, 0x5698B7064EE18DBFUL, 0x5392B72A9103FD5EUL, 0xAA552D645CF92326UL);

        var q = new MegaRandom(0x0123456789ABCDEFUL);
        Assert.Equal(0xA2C2A42038D4EC3DUL, q.NextULong());
        Assert.Equal(0x05FC25D0738E7B0FUL, q.NextULong());
        Assert.Equal(0x625E7BFF938E701EUL, q.NextULong());
        Assert.Equal(0x1BA4DDC6FE2B5726UL, q.NextULong());
        Assert.Equal(0xDF0A2482AC9254CFUL, q.NextULong());
        Assert.Equal(0x3939EDA866ABABE8UL, q.NextULong());
        AssertState(q, 0xE072E993228EBB53UL, 0x172FE4DEACB2AA15UL, 0xD34179BC8A6E2EDCUL, 0x0D21CD63B39FA3CFUL);
    }

    public static void Test_Rng_MegaRandomDerivedOutputsKnownAnswers()
    {
        var d = new MegaRandom(0UL);
        Assert.Equal(0x3FE33D8BE6D96EBEUL, Bits(d.NextDouble()));
        Assert.Equal(0x3FE7EDC3EF092AC8UL, Bits(d.NextDouble()));
        Assert.Equal(0x3FBA5F849D4933E0UL, Bits(d.NextDouble()));
        Assert.Equal(0x3FDAA9653C498B4AUL, Bits(d.NextDouble()));

        Assert.Equal(5415695640260286.0 / 9007199254740992.0, new MegaRandom(0UL).NextDouble());

        var f = new MegaRandom(0UL);
        Assert.Equal(0x3F19EC5FU, Bits(f.NextFloat()));
        Assert.Equal(0x3F3F6E1FU, Bits(f.NextFloat()));
        Assert.Equal(0x3DD2FC20U, Bits(f.NextFloat()));
        Assert.Equal(0x3ED54B28U, Bits(f.NextFloat()));

        Assert.Equal(10087519f / 16777216f, new MegaRandom(0UL).NextFloat());

        var i = new MegaRandom(0UL);
        Assert.SequenceEqual(new[] { 1291202459, 1605832636, 221233742, 894618232 },
            new[] { i.NextInt(), i.NextInt(), i.NextInt(), i.NextInt() });

        var u = new MegaRandom(0UL);
        Assert.SequenceEqual(new[] { 0xCB75F2B4U, 0x4956452AU, 0x4933E6E0U, 0x262D2D2CU },
            new[] { u.NextUInt(), u.NextUInt(), u.NextUInt(), u.NextUInt() });

        var b = new MegaRandom(0UL);
        var bools = new bool[8];
        for (int k = 0; k < bools.Length; k++)
        {
            bools[k] = b.NextBool();
        }
        Assert.SequenceEqual(new[] { true, true, false, false, true, true, false, true }, bools);
    }

    public static void Test_Rng_MegaRandomBoundedKnownAnswers()
    {
        var r = new MegaRandom(0UL);
        var tens = new int[8];
        for (int k = 0; k < tens.Length; k++)
        {
            tens[k] = r.Next(10);
        }
        Assert.SequenceEqual(new[] { 6, 7, 1, 4, 7, 9, 4, 5 }, tens);

        var m = new MegaRandom(0UL);
        Assert.Equal(0, m.Next(1));
        Assert.Equal(1605832635, m.Next(int.MaxValue));
        Assert.Equal(221233742, m.Next(int.MaxValue));

        var s = new MegaRandom(0UL);
        var signed = new int[6];
        for (int k = 0; k < signed.Length; k++)
        {
            signed[k] = s.Next(-5, 5);
        }
        Assert.SequenceEqual(new[] { 1, 2, -4, -1, 2, 4 }, signed);

        var wide = new MegaRandom(0UL);
        Assert.Equal(434921270, wide.Next(int.MinValue, int.MaxValue));
        Assert.Equal(1064181623, wide.Next(int.MinValue, int.MaxValue));
        Assert.Equal(-1705016163, wide.Next(int.MinValue, int.MaxValue));
    }

    public static void Test_Rng_MegaRandomInvalidBoundsThrowWithoutAdvancing()
    {
        var r = new MegaRandom(9UL);
        var before = r.ToSnapshot(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Next(-3));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Next(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Next(6, 5));
        Assert.Equal(before, r.ToSnapshot(0));
    }

    public static void Test_Rng_MegaRandomNextBytesMatchesOutputStream()
    {
        var r = new MegaRandom(0UL);
        var buffer = new byte[13];
        r.NextBytes(buffer);
        Assert.SequenceEqual(new byte[] { 0xB4, 0xF2, 0x75, 0xCB, 0x36, 0x5F, 0xEC, 0x99, 0x2A, 0x45, 0x56, 0x49, 0x78 }, buffer);
        AssertState(r, 0x42CCF76E969D9EDDUL, 0x267E53E3C2B94C43UL, 0x7A748DF3423CA157UL, 0xB6ED46C3EF32A7CEUL);

        var twoSteps = new MegaRandom(0UL);
        twoSteps.NextULong();
        twoSteps.NextULong();
        AssertState(r, twoSteps.S0, twoSteps.S1, twoSteps.S2, twoSteps.S3);

        var full = new MegaRandom(0UL);
        var sixteen = new byte[16];
        full.NextBytes(sixteen);
        Assert.SequenceEqual(new byte[] { 0xB4, 0xF2, 0x75, 0xCB, 0x36, 0x5F, 0xEC, 0x99, 0x2A, 0x45, 0x56, 0x49, 0x78, 0x1F, 0x6E, 0xBF }, sixteen);
        AssertState(full, twoSteps.S0, twoSteps.S1, twoSteps.S2, twoSteps.S3);

        var none = new MegaRandom(0UL);
        none.NextBytes(Span<byte>.Empty);
        AssertState(none, 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL, 0xF88BB8A8724C81ECUL);
    }

    public static void Test_Rng_WrapperIntegerKnownAnswers()
    {
        var b = new Rng(42UL);
        var bools = new bool[8];
        for (int k = 0; k < bools.Length; k++)
        {
            bools[k] = b.NextBool();
        }
        Assert.SequenceEqual(new[] { true, true, false, false, false, false, false, false }, bools);
        Assert.Equal(8, b.Counter);

        var r = new Rng(42UL);
        Assert.SequenceEqual(new[] { 8, 37, 68, 92, 99 },
            new[] { r.NextInt(100), r.NextInt(100), r.NextInt(100), r.NextInt(100), r.NextInt(100) });
        Assert.SequenceEqual(new[] { 7, 7, 8, 7, 6 },
            new[] { r.NextInt(3, 9), r.NextInt(3, 9), r.NextInt(3, 9), r.NextInt(3, 9), r.NextInt(3, 9) });
        Assert.Equal(10, r.Counter);

        var d = new Rng(42UL);
        Assert.Equal(180094358, d.NextInt());
        Assert.Equal(813853890, d.NextInt());
    }

    public static void Test_Rng_WrapperUnsignedKnownAnswers()
    {
        var r = new Rng(42UL);
        Assert.Equal(360188717U, r.NextUnsignedInt());
        Assert.Equal(1627707782U, r.NextUnsignedInt());
        Assert.Equal(680U, r.NextUnsignedInt(1000U));
        Assert.Equal(924U, r.NextUnsignedInt(1000U));
        Assert.Equal(19U, r.NextUnsignedInt(10U, 20U));
        Assert.Equal(17U, r.NextUnsignedInt(10U, 20U));
        Assert.Equal(0xB82154855A65DDB2UL, r.NextUnsignedLong());
        Assert.Equal(0xD99A2743EBE60087UL, r.NextUnsignedLong());
        Assert.Equal(0xC2E96E726E97647EUL, r.NextUnsignedLong(ulong.MaxValue));
        Assert.Equal(583UL, r.NextUnsignedLong(1000UL));
        Assert.Equal(682UL, r.NextUnsignedLong(1000UL));
        Assert.Equal(290677761767UL, r.NextUnsignedLong(5UL, 1000000000000UL));
        Assert.Equal(801024297529UL, r.NextUnsignedLong(5UL, 1000000000000UL));
        Assert.Equal(13, r.Counter);
    }

    public static void Test_Rng_WrapperFloatingKnownAnswers()
    {
        var r = new Rng(42UL);
        Assert.Equal(0x3DABC059U, Bits(r.NextFloat()));
        Assert.Equal(0x3EC209B3U, Bits(r.NextFloat()));
        Assert.Equal(0x3F2E1753U, Bits(r.NextFloat()));
        Assert.Equal(0x4113F36CU, Bits(r.NextFloat(10f)));
        Assert.Equal(0x411EB04AU, Bits(r.NextFloat(10f)));
        Assert.Equal(0x3F0A1B4AU, Bits(r.NextFloat(-1f, 1f)));
        Assert.Equal(0x3EE08552U, Bits(r.NextFloat(-1f, 1f)));
        Assert.Equal(0x3FEB3344E87D7CC0UL, Bits(r.NextDouble()));
        Assert.Equal(0x3FE85D2DCE4DD2ECUL, Bits(r.NextDouble()));
        Assert.Equal(0xBFC2A7A80D88CCD0UL, Bits(r.NextDouble(-3.5, 2.25)));
        Assert.Equal(0x3FDB24851AD15750UL, Bits(r.NextDouble(-3.5, 2.25)));
        Assert.Equal(11, r.Counter);
    }

    public static void Test_Rng_GaussianValuesAndConsumption()
    {
        var a = new Rng(42UL);
        double value = a.NextGaussianDouble(0.5, 0.25, 0.0, 1.0);
        Assert.True(Math.Abs(value - 0.42418423383031545) < 1e-12, $"gaussian double {value:R}");
        Assert.Equal(2, a.Counter);

        var b = new Rng(42UL);
        var sevens = new int[5];
        for (int k = 0; k < sevens.Length; k++)
        {
            sevens[k] = b.NextGaussianInt(7, 1, 6, 7);
        }
        Assert.SequenceEqual(new[] { 7, 6, 7, 7, 6 }, sevens);
        Assert.Equal(30, b.Counter);

        var c = new Rng(42UL);
        var twelves = new int[5];
        for (int k = 0; k < twelves.Length; k++)
        {
            twelves[k] = c.NextGaussianInt(12, 1, 10, 14);
        }
        Assert.SequenceEqual(new[] { 12, 13, 13, 13, 11 }, twelves);
        Assert.Equal(12, c.Counter);

        var d = new Rng(42UL);
        double unit = d.NextGaussianDouble();
        Assert.True(Math.Abs(unit - 0.38346179126769436) < 1e-12, $"gaussian default {unit:R}");
        Assert.Equal(6, d.Counter);

        var e = new Rng(42UL);
        float single = e.NextGaussianFloat();
        Assert.True(Math.Abs(single - 0.3834618f) < 1e-6f, $"gaussian float {single:R}");
        Assert.Equal(6, e.Counter);

        var f = new Rng(42UL);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.NextGaussianDouble(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.NextGaussianDouble(0.5, 1.0, 2.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.NextGaussianInt(20, 1, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.NextGaussianInt(5, 1, 10, 0));
        Assert.Equal(0, f.Counter);
    }

    public static void Test_Rng_ShuffleKnownPermutation()
    {
        var list = Enumerable.Range(0, 10).ToList();
        var r = new Rng(42UL);
        r.Shuffle(list);
        Assert.SequenceEqual(new[] { 9, 1, 4, 2, 8, 7, 6, 5, 3, 0 }, list);
        Assert.Equal(9, r.Counter);
        AssertState(r.Kernel, 0xA6A68565D871A6D8UL, 0xA23425F8F32D20B3UL, 0xAED207CADDB3FE5BUL, 0xDF4D732ABA2FF1AAUL);

        var unstable = Enumerable.Range(0, 10).ToList();
        var u = new Rng(42UL);
        var returned = unstable.UnstableShuffle(u);
        Assert.True(ReferenceEquals(unstable, returned));
        Assert.SequenceEqual(list, unstable);
        Assert.Equal(r.ToSnapshot(), u.ToSnapshot());

        var tiny = new Rng(42UL);
        new List<int>().UnstableShuffle(tiny);
        new List<int> { 1 }.UnstableShuffle(tiny);
        tiny.Shuffle(new List<int> { 1 });
        tiny.Shuffle(Array.Empty<int>());
        Assert.Equal(0, tiny.Counter);
    }

    public static void Test_Rng_StableShuffleSortsThenShuffles()
    {
        var list = new List<int> { 5, 3, 9, 1, 7, 3, 0, 8, 2, 6 };
        var r = new Rng(42UL);
        list.StableShuffle(r);
        Assert.SequenceEqual(new[] { 9, 1, 3, 2, 8, 7, 6, 5, 3, 0 }, list);
        Assert.Equal(9, r.Counter);

        var viaComparer = new List<int> { 5, 3, 9, 1, 7, 3, 0, 8, 2, 6 };
        viaComparer.StableShuffle(new Rng(42UL), Comparer<int>.Default);
        Assert.SequenceEqual(list, viaComparer);

        var viaComparison = new List<int> { 5, 3, 9, 1, 7, 3, 0, 8, 2, 6 };
        viaComparison.StableShuffle(new Rng(42UL), static (x, y) => x.CompareTo(y));
        Assert.SequenceEqual(list, viaComparison);

        var tagged = new List<(int Key, string Tag)> { (1, "b1"), (1, "b2"), (0, "a") };
        var expected = new List<(int Key, string Tag)>(tagged);
        expected.Sort(static (x, y) => x.Key.CompareTo(y.Key));
        Assert.SequenceEqual(new[] { (0, "a"), (1, "b2"), (1, "b1") }, expected,
            "List.Sort 3-element network (SwapIfGreater 0-1, 0-2, 1-2) must match the game runtime");
        var shadow = new Rng(7UL);
        expected.UnstableShuffle(shadow);
        tagged.StableShuffle(new Rng(7UL), static (x, y) => x.Key.CompareTo(y.Key));
        Assert.SequenceEqual(expected, tagged);
    }

    public static void Test_Rng_TakeRandomShufflesWholeCollectionEagerly()
    {
        var r = new Rng(42UL);
        var taken = Enumerable.Range(0, 10).TakeRandom(3, r);
        Assert.Equal(9, r.Counter);
        Assert.SequenceEqual(new[] { 9, 1, 4 }, taken.ToList());
        Assert.Equal(9, r.Counter);
    }

    public static void Test_Rng_NextItemKnownAnswers()
    {
        var items = new[] { "a", "b", "c", "d", "e" };
        var r = new Rng(42UL);
        Assert.SequenceEqual(new[] { "a", "b", "d", "e" },
            new[] { r.NextItem(items), r.NextItem(items), r.NextItem(items), r.NextItem(items) });
        Assert.Equal(4, r.Counter);

        IEnumerable<string> lazy = items.Select(static s => s);
        var q = new Rng(42UL);
        Assert.SequenceEqual(new[] { "a", "b", "d", "e" },
            new[] { q.NextItem(lazy), q.NextItem(lazy), q.NextItem(lazy), q.NextItem(lazy) });
        Assert.Equal(4, q.Counter);

        var empty = new Rng(42UL);
        Assert.Null(empty.NextItem(Array.Empty<string>()));
        Assert.Null(empty.NextItem(Enumerable.Empty<string>()));
        Assert.Equal(0, empty.NextItem(new List<int>()));
        Assert.Equal(0, empty.Counter);
    }

    public static void Test_Rng_WeightedNextItemKnownAnswers()
    {
        var items = new[] { "a", "b", "c", "d" };
        static float Weight(string? s) => s switch
        {
            "a" => 1f,
            "b" => 2f,
            "c" => 3f,
            "d" => 0.5f,
            _ => 0f
        };

        var r = new Rng(42UL);
        var picks = new string?[6];
        for (int k = 0; k < picks.Length; k++)
        {
            picks[k] = r.WeightedNextItem(items, Weight);
        }
        Assert.SequenceEqual(new[] { "a", "b", "c", "d", "d", "c" }, picks);
        Assert.Equal(6, r.Counter);

        IEnumerable<string> lazy = items.Select(static s => s);
        var q = new Rng(42UL);
        var lazyPicks = new string?[6];
        for (int k = 0; k < lazyPicks.Length; k++)
        {
            lazyPicks[k] = q.WeightedNextItem(lazy, Weight);
        }
        Assert.SequenceEqual(picks, lazyPicks);
        Assert.Equal(6, q.Counter);

        Assert.Equal("d", Rng.WeightedNextItem(0.9246929f, items, static s => Weight(s), "fallback"));
        Assert.Equal("a", Rng.WeightedNextItem(0f, items, static s => Weight(s), "fallback"));
        Assert.Equal("fallback", Rng.WeightedNextItem(0.5f, Array.Empty<string>(), static s => Weight(s), "fallback"));

        var e = new Rng(42UL);
        Assert.Null(e.WeightedNextItem(Array.Empty<string>(), Weight));
        Assert.Equal(1, e.Counter);
    }

    public static void Test_Rng_CounterSemantics()
    {
        var r = new Rng(1UL);
        r.NextUnsignedInt();
        Assert.Equal(1, r.Counter);
        r.NextUnsignedInt(5U);
        Assert.Equal(2, r.Counter);
        r.NextUnsignedLong(ulong.MaxValue);
        Assert.Equal(3, r.Counter);
        r.NextUnsignedLong(10UL);
        Assert.Equal(4, r.Counter);
        r.NextFloat();
        Assert.Equal(5, r.Counter);
        r.NextFloat(3f);
        Assert.Equal(6, r.Counter);
        Assert.Equal(2f, r.NextFloat(2f, 2f));
        Assert.Equal(7, r.Counter);

        RngSnapshot before = r.ToSnapshot();
        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextUnsignedInt(5U, 5U));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextUnsignedLong(9UL, 3UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextFloat(2f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextDouble(2.0, 1.0));
        Assert.Equal(before, r.ToSnapshot());

        Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(0));
        Assert.Equal(before with { Counter = before.Counter + 1 }, r.ToSnapshot());

        var k = new Rng(77UL);
        var shadow = new MegaRandom(k.ToSnapshot());
        k.NextBool();
        k.NextInt(9);
        k.NextInt(-3, 3);
        k.NextUnsignedInt();
        k.NextUnsignedLong();
        k.NextFloat();
        k.NextDouble();
        k.NextDouble(0.0, 5.0);
        for (int i = 0; i < 8; i++)
        {
            shadow.NextULong();
        }
        Assert.Equal(8, k.Counter);
        AssertState(k.Kernel, shadow.S0, shadow.S1, shadow.S2, shadow.S3);
    }

    public static void Test_Rng_SnapshotRoundTrip()
    {
        var a = new Rng(2024UL, "shuffle");
        for (int i = 0; i < 17; i++)
        {
            a.NextInt(52);
        }
        RngSnapshot snap = a.ToSnapshot();
        Assert.Equal(17, snap.Counter);
        Assert.Equal(a.Kernel.S0, snap.S0);
        Assert.Equal(a.Kernel.S3, snap.S3);

        var b = new Rng(snap);
        Assert.Equal(snap, b.ToSnapshot());
        var c = new Rng(1UL);
        c.LoadFromSnapshot(snap);
        Assert.Equal(snap, c.ToSnapshot());
        for (int i = 0; i < 20; i++)
        {
            double expected = a.NextDouble();
            Assert.Equal(expected, b.NextDouble());
            Assert.Equal(expected, c.NextDouble());
        }
        Assert.Equal(a.ToSnapshot(), b.ToSnapshot());

        Assert.Equal("Counter: 3 State: 1 2 3 4", new RngSnapshot(3, 1UL, 2UL, 3UL, 4UL).ToString());
        var kernel = new MegaRandom(new RngSnapshot(9, 5UL, 6UL, 7UL, 8UL));
        AssertState(kernel, 5UL, 6UL, 7UL, 8UL);
        Assert.Equal(new RngSnapshot(11, 5UL, 6UL, 7UL, 8UL), kernel.ToSnapshot(11));
    }

    public static void Test_Rng_DeepCloneIsIndependent()
    {
        var a = new Rng(5UL, "monster_ai");
        a.NextInt(10);
        Rng b = a.DeepClone();
        Assert.False(ReferenceEquals(a, b));
        Assert.False(ReferenceEquals(a.Kernel, b.Kernel));
        Assert.Equal(a.ToSnapshot(), b.ToSnapshot());
        b.NextInt(10);
        b.NextInt(10);
        Assert.Equal(1, a.Counter);
        Assert.Equal(3, b.Counter);
        Assert.NotEqual(a.ToSnapshot(), b.ToSnapshot());

        var k = new MegaRandom(3UL);
        MegaRandom kc = k.DeepClone();
        kc.NextULong();
        AssertState(new MegaRandom(3UL), k.S0, k.S1, k.S2, k.S3);
    }

    public static void Test_Rng_XxHash64KnownVectors()
    {
        Assert.Equal(0xEF46DB3751D8E999UL, DeterministicHash.GetDeterministicHashCode(""));
        Assert.Equal(0xD24EC4F1A98C6E5BUL, DeterministicHash.GetDeterministicHashCode("a"));
        Assert.Equal(0x44BC2CF5AD770999UL, DeterministicHash.GetDeterministicHashCode("abc"));
        Assert.Equal(0xFBCEA83C8A378BF1UL, DeterministicHash.GetDeterministicHashCode("Nobody inspects the spammish repetition"));
        Assert.Equal(0x51ACEF020CD423B1UL, DeterministicHash.GetDeterministicHashCode("0123456789ABCDEF0123456789ABCDEF"));
        Assert.Equal(0x2C72EDB08AC63D8CUL, DeterministicHash.GetDeterministicHashCode(UnicodeSample()));
        Assert.Equal(0x7C7021C5035C8FADUL, DeterministicHash.GetDeterministicHashCode(OracleSeed));
        Assert.Equal(0x1AEC5A643D318478UL, DeterministicHash.GetDeterministicHashCode("FUR_COAT"));
        Assert.Equal(0x496DDEC910227D94UL, DeterministicHash.GetDeterministicHashCode(LongAscii()));
        Assert.Equal(0x90200B144DDCCD65UL, DeterministicHash.GetDeterministicHashCode(new string((char)0xE9, 200)));
        Assert.Equal(0xB559B98D844E0635UL, DeterministicHash.XxHash64("xxhash"u8, 20141025L));
        Assert.Equal(0xEF46DB3751D8E999UL, DeterministicHash.XxHash64(ReadOnlySpan<byte>.Empty));
    }

    public static void Test_Rng_HashDeterministicAcrossCalls()
    {
        string[] inputs =
        {
            "", "a", OracleSeed, LongAscii(), "up_front", new string((char)0xE9, 200), "monster_ai",
            UnicodeSample(), "treasure_room_relics"
        };
        var first = new ulong[inputs.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            first[i] = DeterministicHash.GetDeterministicHashCode(inputs[i]);
        }
        for (int round = 0; round < 3; round++)
        {
            for (int i = inputs.Length - 1; i >= 0; i--)
            {
                Assert.Equal(first[i], DeterministicHash.GetDeterministicHashCode(inputs[i]), inputs[i]);
                Assert.Equal(first[i], DeterministicHash.GetDeterministicHashCode(new string(inputs[i].AsSpan())), inputs[i]);
            }
        }
        Assert.Equal(DeterministicHash.XxHash64(System.Text.Encoding.UTF8.GetBytes(LongAscii())),
            DeterministicHash.GetDeterministicHashCode(LongAscii()));
    }

    public static void Test_Rng_HashOldKnownAnswers()
    {
        Assert.Equal(757602046, DeterministicHash.GetDeterministicHashCodeOld(""));
        Assert.Equal(-842352707, DeterministicHash.GetDeterministicHashCodeOld("a"));
        Assert.Equal(938310215, DeterministicHash.GetDeterministicHashCodeOld("ab"));
        Assert.Equal(-320589142, DeterministicHash.GetDeterministicHashCodeOld("abc"));
        Assert.Equal(-1234747045, DeterministicHash.GetDeterministicHashCodeOld("up_front"));
        Assert.Equal(-853278557, DeterministicHash.GetDeterministicHashCodeOld("3URCWVGADS"));
    }

    public static void Test_Rng_StreamNamesAndSlugs()
    {
        string[] runNames =
        {
            "up_front", "shuffle", "unknown_map_point", "combat_card_generation", "combat_potion_generation",
            "combat_card_selection", "combat_energy_costs", "combat_targets", "monster_ai", "niche", "combat_orbs",
            "treasure_room_relics"
        };
        Assert.Equal(runNames.Length, RunRngSet.StreamCount);
        for (int i = 0; i < runNames.Length; i++)
        {
            var type = (RunRngType)i;
            Assert.Equal(runNames[i], DeterministicHash.SnakeCase(type.ToString()));
            Assert.Equal(runNames[i], RunRngSet.StreamName(type));
        }

        string[] playerNames = { "rewards", "shops", "transformations" };
        Assert.Equal(playerNames.Length, PlayerRngSet.StreamCount);
        for (int i = 0; i < playerNames.Length; i++)
        {
            var type = (PlayerRngType)i;
            Assert.Equal(playerNames[i], DeterministicHash.SnakeCase(type.ToString()));
            Assert.Equal(playerNames[i], PlayerRngSet.StreamName(type));
        }

        Assert.Equal("STRIKE_IRONCLAD", DeterministicHash.Slugify("StrikeIronclad"));
        Assert.Equal("NIBBITS_WEAK", DeterministicHash.Slugify("NibbitsWeak"));
        Assert.Equal("TWIN_STRIKE", DeterministicHash.Slugify("  Twin Strike "));
        Assert.Equal("a_b_c", DeterministicHash.SnakeCase("ABC"));
    }

    public static void Test_Rng_StreamNameHashesSeedNamedStreams()
    {
        ulong[] runHashes =
        {
            0x1A8A7B60DA243E80UL, 0x2AD1D778C87C9714UL, 0x9800EF3164594356UL, 0xA2A21EEF44216B8EUL,
            0x9EA3163CFEB05AC2UL, 0x54A326EC000E7A0AUL, 0xB2AEE0C22BB1887FUL, 0x5FF62452AC6B3AC2UL,
            0x5ACEEC82DAC3A1A7UL, 0x1713D477D45AFAC5UL, 0x917D243ADDB36D52UL, 0x5E4180A0A992AED6UL
        };
        var set = new RunRngSet(12345UL);
        Assert.Equal(12345UL, set.Seed);
        Assert.Equal(string.Empty, set.StringSeed);
        for (int i = 0; i < runHashes.Length; i++)
        {
            var type = (RunRngType)i;
            Assert.Equal(runHashes[i], DeterministicHash.GetDeterministicHashCode(RunRngSet.StreamName(type)), type.ToString());
            Assert.Equal(new Rng(12345UL + runHashes[i]).ToSnapshot(), set.Get(type).ToSnapshot(), type.ToString());
            Assert.True(ReferenceEquals(set.Get(type), set.GetRng(type)));
        }
        Assert.Equal(new Rng(0x1A8A7B60DA246EB9UL).ToSnapshot(), set.UpFront.ToSnapshot());
        Assert.True(ReferenceEquals(set.Get(RunRngType.CombatOrbs), set.CombatOrbGeneration));
        Assert.True(ReferenceEquals(set.Get(RunRngType.MonsterAi), set.MonsterAi));
        Assert.True(ReferenceEquals(set.Get(RunRngType.Shuffle), set.Shuffle));

        ulong[] playerHashes = { 0x20F3BF5504146BB3UL, 0xDA13890C3383CB6BUL, 0xB8623D6522D877F5UL };
        var players = new PlayerRngSet(777UL);
        for (int i = 0; i < playerHashes.Length; i++)
        {
            var type = (PlayerRngType)i;
            Assert.Equal(playerHashes[i], DeterministicHash.GetDeterministicHashCode(PlayerRngSet.StreamName(type)));
            Assert.Equal(new Rng(777UL + playerHashes[i]).ToSnapshot(), players.Get(type).ToSnapshot());
        }
        Assert.True(ReferenceEquals(players.Get(PlayerRngType.Rewards), players.Rewards));
        Assert.True(ReferenceEquals(players.Get(PlayerRngType.Shops), players.Shops));
        Assert.True(ReferenceEquals(players.Get(PlayerRngType.Transformations), players.Transformations));
    }

    public static void Test_Rng_RunSeedPaths()
    {
        Assert.Equal(OracleSeedHash, RngSeeds.RunSeed(OracleSeed));
        Assert.Equal(3441688739UL, RngSeeds.RunSeed("old3URCWVGADS"));
        Assert.Equal(0xEF46DB3751D8E999UL, RngSeeds.RunSeed(string.Empty));

        var normal = new RunRngSet(OracleSeed);
        Assert.Equal(OracleSeed, normal.StringSeed);
        Assert.Equal(OracleSeedHash, normal.Seed);

        var old = new RunRngSet("old3URCWVGADS");
        Assert.Equal("old3URCWVGADS", old.StringSeed);
        Assert.Equal(3441688739UL, old.Seed);
        Assert.Equal(new Rng(3441688739UL, "shuffle").ToSnapshot(), old.Shuffle.ToSnapshot());

        Assert.Equal("Q3X6PF8S37GA", RngSeeds.CanonicalizeSeed(" q3x6pf8s37ga "));
        Assert.Equal("ABC01", RngSeeds.CanonicalizeSeed("abcoi"));
    }

    public static void Test_Rng_CompositeSeedFormulasWrap()
    {
        Assert.Equal(0x975C7C29408E1425UL, RngSeeds.PlayerModel(OracleSeedHash, 0, "FUR_COAT"));
        Assert.Equal(0x975C7C29408E142DUL, RngSeeds.PlayerModel(OracleSeedHash, 1, "FUR_COAT", 7UL));
        Assert.Equal(0xD54E5BB438AEAE47UL, RngSeeds.PlayerModel(ulong.MaxValue, -1, "BYRDPIP", ulong.MaxValue));
        Assert.Equal(new Rng(0x975C7C29408E1425UL).ToSnapshot(),
            Rng.ForPlayerModel(new RunRngSet(OracleSeed), 0, "FUR_COAT").ToSnapshot());
        Assert.Equal(new Rng(0x975C7C29408E142DUL).ToSnapshot(),
            Rng.ForPlayerModel(OracleSeedHash, 1, "FUR_COAT", 7UL).ToSnapshot());

        Assert.Equal(9UL, RngSeeds.Monster(ulong.MaxValue - 1UL, 3, 5, 1, 2U));
        Assert.Equal(0x7C7021C5035C8FAEUL, RngSeeds.Monster(OracleSeedHash, 0, 0, 0, 1U));
        Assert.Equal(0xB905553B3BC6AEDDUL, RngSeeds.Encounter(OracleSeedHash, 1, "SHRINKER_BEETLE_WEAK"));
        Assert.Equal(0x9608BE7F570CEE1BUL, RngSeeds.Event(OracleSeedHash, false, 1, "NEOW"));
        Assert.Equal(0x9608BE7F570CEE1AUL, RngSeeds.Event(OracleSeedHash, true, 1, "NEOW"));
        Assert.Equal(0x7C7021C5035C8FAEUL, RngSeeds.PlayerSet(OracleSeed, 1));
        Assert.Equal(0x7C7021C5035C8FACUL, RngSeeds.PlayerSet(OracleSeed, -1));
        Assert.Equal(OracleSeedHash + 2UL, RngSeeds.PlayerSetFromHashedRunSeed(OracleSeedHash, 2));
        Assert.Equal(0x1A8A7B60DA243E7FUL, RngSeeds.Named(ulong.MaxValue, "up_front"));
        Assert.Equal(new Rng(0x1A8A7B60DA243E7FUL).ToSnapshot(), new Rng(ulong.MaxValue, "up_front").ToSnapshot());
    }

    public static void Test_Rng_GameSaveOracleRunStreams()
    {
        var set = new RunRngSet(OracleSeed);
        Assert.Equal(OracleSeedHash, set.Seed);
        foreach (var e in OracleRunStreams)
        {
            Rng rng = set.Get(e.Type);
            for (int i = 0; i < e.Counter; i++)
            {
                rng.NextUnsignedLong();
            }
            Assert.Equal(new RngSnapshot(e.Counter, e.S0, e.S1, e.S2, e.S3), rng.ToSnapshot(), e.Type.ToString());
        }
    }

    public static void Test_Rng_GameSaveOraclePlayerStreams()
    {
        var set = PlayerRngSet.ForPlayer(OracleSeed, 0);
        Assert.Equal(OracleSeedHash, set.Seed);
        foreach (var e in OraclePlayerStreams)
        {
            Rng rng = set.Get(e.Type);
            for (int i = 0; i < e.Counter; i++)
            {
                rng.NextUnsignedLong();
            }
            Assert.Equal(new RngSnapshot(e.Counter, e.S0, e.S1, e.S2, e.S3), rng.ToSnapshot(), e.Type.ToString());
        }

        Assert.Equal(3441688739UL, (ulong)unchecked((uint)DeterministicHash.GetDeterministicHashCodeOld("3URCWVGADS")));
    }

    public static void Test_Rng_LegacyV18Kernel()
    {
        ulong signExtended = unchecked((ulong)(long)DeterministicHash.GetDeterministicHashCodeOld("3URCWVGADS"));
        Assert.Equal(0xFFFFFFFFCD2400A3UL, signExtended);
        AssertState(RngSeeds.LegacyV18Kernel(signExtended, "up_front", 0),
            0x44F4FA92E3AD0E7CUL, 0x26C50CBB5FDC84C4UL, 0x2CC9628CBEE2EF9EUL, 0x8146CA7C39AE53B1UL);
        AssertState(new MegaRandom(2206941694UL),
            0x44F4FA92E3AD0E7CUL, 0x26C50CBB5FDC84C4UL, 0x2CC9628CBEE2EF9EUL, 0x8146CA7C39AE53B1UL);
        AssertState(RngSeeds.LegacyV18Kernel(signExtended, "shuffle", 5),
            0x5709160AE24945FDUL, 0x30C15BC24685E6D5UL, 0x85BBD4FE163A3169UL, 0xC07A7504695C8659UL);
        AssertState(RngSeeds.LegacyV18Kernel(3441688739UL, "rewards", 3),
            0x98CF86058239E856UL, 0x6B753C34AEBDCAB1UL, 0x91873FA2D8540C51UL, 0x5A6534FD6FCAD978UL);
    }

    public static void Test_Rng_RunRngSetMockSeams()
    {
        var shared = new Rng(99UL);
        var set = new RunRngSet(shared);
        Assert.Equal(0UL, set.Seed);
        Assert.Equal(string.Empty, set.StringSeed);
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            Assert.True(ReferenceEquals(shared, set.Get((RunRngType)i)));
            Assert.Equal(0, set.AliasOf((RunRngType)i));
        }
        set.Shuffle.NextInt(10);
        Assert.Equal(1, shared.Counter);
        Assert.Equal(1, set.MonsterAi.Counter);

        set.MockRng(RunRngType.MonsterAi, 1234UL);
        Assert.False(ReferenceEquals(shared, set.MonsterAi));
        Assert.Equal(new Rng(1234UL).ToSnapshot(), set.MonsterAi.ToSnapshot());
        Assert.Equal((int)RunRngType.MonsterAi, set.AliasOf(RunRngType.MonsterAi));
        Assert.Equal(0, set.AliasOf(RunRngType.Niche));
        set.Niche.NextBool();
        Assert.Equal(0, set.MonsterAi.Counter);
        Assert.Equal(2, shared.Counter);

        var mock = RunRngSet.CreateMockInstance();
        Assert.Equal(0xEF46DB3751D8E999UL, mock.Seed);
        Assert.Equal(string.Empty, mock.StringSeed);
        Assert.Equal(new RngSnapshot(0, 0xFF951101EEAB724FUL, 0x195EDD174587F5FDUL, 0xE12D75AE6E1F9B73UL, 0xA9188855B92BA073UL),
            mock.UpFront.ToSnapshot());
        Assert.False(ReferenceEquals(mock, RunRngSet.CreateMockInstance()));
    }

    public static void Test_Rng_RunRngSetDeepClonePreservesAliasing()
    {
        var shared = new Rng(5UL);
        var set = new RunRngSet(shared);
        set.MockRng(RunRngType.Shuffle, 77UL);
        RunRngSet clone = set.DeepClone();

        Rng cloneShared = clone.UpFront;
        Assert.False(ReferenceEquals(shared, cloneShared));
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            var type = (RunRngType)i;
            Assert.Equal(set.AliasOf(type), clone.AliasOf(type), type.ToString());
            if (type != RunRngType.Shuffle)
            {
                Assert.True(ReferenceEquals(cloneShared, clone.Get(type)), type.ToString());
            }
        }
        Assert.False(ReferenceEquals(set.Shuffle, clone.Shuffle));
        Assert.False(ReferenceEquals(cloneShared, clone.Shuffle));

        clone.Niche.NextInt(10);
        Assert.Equal(0, shared.Counter);
        Assert.Equal(1, clone.UpFront.Counter);
        Assert.Equal(0, clone.Shuffle.Counter);

        var independent = new RunRngSet(OracleSeed);
        independent.Shuffle.NextInt(10);
        RunRngSet independentClone = independent.DeepClone();
        Assert.Equal(OracleSeed, independentClone.StringSeed);
        Assert.Equal(OracleSeedHash, independentClone.Seed);
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            var type = (RunRngType)i;
            Assert.False(ReferenceEquals(independent.Get(type), independentClone.Get(type)));
            Assert.Equal(independent.Get(type).ToSnapshot(), independentClone.Get(type).ToSnapshot());
            Assert.Equal(i, independentClone.AliasOf(type));
        }
        independentClone.Shuffle.NextInt(10);
        Assert.Equal(1, independent.Shuffle.Counter);
        Assert.Equal(2, independentClone.Shuffle.Counter);
    }

    public static void Test_Rng_RunRngSetSnapshotRoundTrip()
    {
        var set = new RunRngSet(OracleSeed);
        set.Shuffle.NextInt(10);
        set.MonsterAi.NextFloat();
        set.Niche.NextBool();
        RunRngSetSnapshot snap = set.ToSnapshot();
        Assert.Equal(OracleSeed, snap.Seed);
        Assert.Equal<ulong?>(OracleSeedHash, snap.HashedSeed);
        Assert.Equal(RunRngSet.StreamCount, snap.Rngs.Count);

        RunRngSet restored = RunRngSet.FromSnapshot(snap);
        var gameStyle = new RunRngSetSnapshot { Seed = snap.Seed, Rngs = new Dictionary<RunRngType, RngSnapshot>(snap.Rngs) };
        RunRngSet restoredFromString = RunRngSet.FromSnapshot(gameStyle);
        var loaded = new RunRngSet(OracleSeed);
        loaded.LoadFromSnapshot(snap);
        Assert.Equal(OracleSeedHash, restored.Seed);
        Assert.Equal(OracleSeedHash, restoredFromString.Seed);
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            var type = (RunRngType)i;
            Assert.Equal(set.Get(type).ToSnapshot(), restored.Get(type).ToSnapshot(), type.ToString());
            Assert.Equal(set.Get(type).ToSnapshot(), restoredFromString.Get(type).ToSnapshot(), type.ToString());
            Assert.Equal(set.Get(type).ToSnapshot(), loaded.Get(type).ToSnapshot(), type.ToString());
        }
        int next = set.Shuffle.NextInt(1000);
        Assert.Equal(next, restored.Shuffle.NextInt(1000));

        Assert.Throws<NotImplementedException>(() => new RunRngSet("OTHERSEED000").LoadFromSnapshot(snap));

        var numeric = new RunRngSet(12345UL);
        numeric.UpFront.NextInt(3);
        RunRngSet numericRestored = RunRngSet.FromSnapshot(numeric.ToSnapshot());
        Assert.Equal(12345UL, numericRestored.Seed);
        Assert.Equal(numeric.UpFront.ToSnapshot(), numericRestored.UpFront.ToSnapshot());
        Assert.Throws<NotImplementedException>(() => new RunRngSet(54321UL).LoadFromSnapshot(numeric.ToSnapshot()));

        RunRngSetSnapshot copy = snap.DeepClone();
        copy.Rngs[RunRngType.Shuffle] = new RngSnapshot(0, 1UL, 2UL, 3UL, 4UL);
        Assert.NotEqual(copy.Rngs[RunRngType.Shuffle], snap.Rngs[RunRngType.Shuffle]);
    }

    public static void Test_Rng_PlayerRngSetSnapshotRoundTrip()
    {
        var set = PlayerRngSet.ForPlayer(OracleSeed, 1);
        Assert.Equal(OracleSeedHash + 1UL, set.Seed);
        set.Rewards.NextInt(5);
        set.Shops.NextDouble();
        PlayerRngSetSnapshot snap = set.ToSnapshot();
        Assert.Equal(set.Seed, snap.Seed);
        Assert.Equal(PlayerRngSet.StreamCount, snap.Rngs.Count);

        PlayerRngSet restored = PlayerRngSet.FromSnapshot(snap);
        var loaded = new PlayerRngSet(set.Seed);
        loaded.LoadFromSnapshot(snap);
        PlayerRngSet clone = set.DeepClone();
        for (int i = 0; i < PlayerRngSet.StreamCount; i++)
        {
            var type = (PlayerRngType)i;
            Assert.Equal(set.Get(type).ToSnapshot(), restored.Get(type).ToSnapshot());
            Assert.Equal(set.Get(type).ToSnapshot(), loaded.Get(type).ToSnapshot());
            Assert.Equal(set.Get(type).ToSnapshot(), clone.Get(type).ToSnapshot());
            Assert.False(ReferenceEquals(set.Get(type), clone.Get(type)));
            Assert.True(ReferenceEquals(set.Get(type), set.GetRng(type)));
        }
        clone.Rewards.NextBool();
        Assert.Equal(1, set.Rewards.Counter);
        Assert.Equal(2, clone.Rewards.Counter);

        Assert.Throws<NotImplementedException>(() => new PlayerRngSet(1UL).LoadFromSnapshot(snap));
    }

    public static void Test_Rng_CanonicalBytesTrackState()
    {
        var a = new Rng(7UL);
        a.NextInt(10);
        Rng b = a.DeepClone();
        Assert.SequenceEqual(Canonical(a), Canonical(b));
        Assert.SequenceEqual(Canonical(a), Canonical(a.ToSnapshot()));
        b.NextInt(10);
        Assert.False(Canonical(a).SequenceEqual(Canonical(b)));

        var k = new MegaRandom(7UL);
        Assert.SequenceEqual(Canonical(k), Canonical(k.DeepClone()));

        var set = new RunRngSet(OracleSeed);
        RunRngSet setClone = set.DeepClone();
        Assert.SequenceEqual(Canonical(set), Canonical(setClone));
        setClone.CombatTargets.NextInt(3);
        Assert.False(Canonical(set).SequenceEqual(Canonical(setClone)));

        var shared = new RunRngSet(new Rng(5UL));
        RunRngSet unshared = RunRngSet.FromSnapshot(shared.ToSnapshot());
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            Assert.Equal(shared.Get((RunRngType)i).ToSnapshot(), unshared.Get((RunRngType)i).ToSnapshot());
        }
        Assert.False(Canonical(shared).SequenceEqual(Canonical(unshared)));
        Assert.SequenceEqual(Canonical(shared), Canonical(shared.DeepClone()));

        var players = PlayerRngSet.ForPlayer(OracleSeed, 0);
        Assert.SequenceEqual(Canonical(players), Canonical(players.DeepClone()));
        Assert.SequenceEqual(Canonical(set.ToSnapshot()), Canonical(set.ToSnapshot().DeepClone()));
        Assert.SequenceEqual(Canonical(players.ToSnapshot()), Canonical(players.ToSnapshot().DeepClone()));
    }

    private static byte[] Canonical(ICanonical value)
    {
        var w = new CanonicalWriter(256);
        value.WriteCanonical(w);
        return w.ToArray();
    }

    private static string LongAscii()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var chars = new char[300];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[i % alphabet.Length];
        }
        return new string(chars);
    }

    private static string UnicodeSample() => new string(new[]
    {
        (char)0x03A9, (char)0x2248, (char)0x00E7, (char)0x221A, ' ', 'h', (char)0x00E9, 'l', 'l', 'o'
    });

    private static ulong Bits(double value) => BitConverter.DoubleToUInt64Bits(value);

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static void AssertState(MegaRandom r, ulong s0, ulong s1, ulong s2, ulong s3)
    {
        Assert.Equal(s0, r.S0, "s0");
        Assert.Equal(s1, r.S1, "s1");
        Assert.Equal(s2, r.S2, "s2");
        Assert.Equal(s3, r.S3, "s3");
    }
}
