using System.Runtime;
using System.Text;
using System.Text.Json;
using Sts2Sim.Core.Random;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Sts2Sim.Vec tests (CONTRACTS.md §D7). Scaffold: schema + ABI. V1 adds encoder/leak/parity tests (T2, T3),
/// V2 adds env/candidate/pool/determinism/thread tests (T1, T4-T8).
/// </summary>
public static unsafe partial class VecTests
{
    /// <summary>§D3.1 buffer order (the void** order).</summary>
    private static readonly string[] BufferOrder =
    {
        "global_i", "global_f", "hand", "hand_f", "hand_dmg", "deck", "draw_counts", "discard_counts", "exhaust_counts",
        "relics", "potions", "powers_player", "orbs", "enemies", "enemy_intents", "enemy_powers", "allies", "ally_powers",
        "map_nodes", "rewards", "card_reward", "card_reward_alts", "shop", "rest", "event_options", "prompt_options",
        "cand", "cand_n", "reward", "terminated", "truncated", "status", "episode", "aux", "last_episode",
        // appended (player-view schema append, docs/parity-audit-2026-11-02.md §5.2)
        "global_x", "hand_x", "draw_marked", "discard_order", "exhaust_order", "event_option_values",
        // appended with the rewards-screen macro candidates (2026-11-04)
        "reward_choices",
        // appended with the map-routing features (2026-11-04)
        "map_routes",
    };

    public static void Test_Vec_SchemaJsonRoundTrips()
    {
        string json = Schema.ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string version = root.GetProperty("encoder_version").GetString()!;
        Assert.Equal(16, version.Length);
        Assert.True(version.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), $"encoder_version '{version}' is not lower-case hex");
        // encoder_version = XXH64 of the JSON without the version member.
        string member = "\"encoder_version\":\"" + version + "\",";
        Assert.True(json.StartsWith("{" + member, StringComparison.Ordinal), "encoder_version must be the first member");
        string body = "{" + json[(1 + member.Length)..];
        Assert.Equal(version, DeterministicHash.XxHash64(Encoding.UTF8.GetBytes(body)).ToString("x16"));
        Assert.Equal(version, Schema.EncoderVersion);

        var buffers = root.GetProperty("buffers").EnumerateArray().ToList();
        Assert.Equal(Buf.Count, buffers.Count);
        Assert.Equal(BufferOrder.Length, buffers.Count, "buffer count (34 numbered rows of D3.1 + card_reward_alts + 6 + 2 appended)");
        int vCards = root.GetProperty("vocab").GetProperty("cards").GetArrayLength();
        for (int k = 0; k < buffers.Count; k++)
        {
            var b = buffers[k];
            string name = b.GetProperty("name").GetString()!;
            Assert.Equal(BufferOrder[k], name, $"buffer {k}");
            Assert.Equal(k, Schema.IndexOf(name));
            var shape = b.GetProperty("shape").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            Assert.SequenceEqual(Schema.Buffers[k].Shape, shape, name);
            int elems = shape.Aggregate(1, (x, y) => x * y);
            int bytes = elems * Schema.SizeOf(b.GetProperty("dtype").GetString()!);
            Assert.True(bytes > 0, $"{name} has size 0");
            Assert.Equal(bytes, Schema.BytesPerEnv(k), name);
            Assert.Equal(bytes, Schema.BytesPerEnv(name), name);
            Assert.Equal(elems, Schema.Elements(name), name);
            var fields = b.GetProperty("fields");
            if (fields.ValueKind == JsonValueKind.Array)
            {
                Assert.Equal(shape[^1], fields.GetArrayLength(), $"{name}: fields name the last axis");
            }
        }
        Assert.Equal(2 * vCards, Schema.Buffers[Buf.DrawCounts].Shape[0]);
        Assert.Equal(Schema.HC, Schema.Buffers[Buf.Hand].Shape[1]);
        Assert.Equal(Schema.HC, Schema.Buffers[Buf.CardReward].Shape[1]);

        var vocab = root.GetProperty("vocab");
        foreach (var kind in new[] { "cards", "relics", "potions", "powers", "monsters", "encounters", "events", "orbs", "enchantments", "afflictions", "acts", "characters" })
        {
            var list = vocab.GetProperty(kind).EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.True(list.Count > 2, kind);
            Assert.Equal("", list[0], kind);
            Assert.Equal("?", list[1], kind);
            var rest = list.Skip(2).ToList();
            Assert.SequenceEqual(rest.OrderBy(s => s, StringComparer.Ordinal), rest, $"{kind} sorted ordinal");
        }
        Assert.True(vCards - 2 >= Sts2Sim.Core.Cards.CardCatalog.Count, "cards include the catalog ids");
        var enums = vocab.GetProperty("enums");
        foreach (var e in new[] { "RunPhase", "RunActionKind", "IntentType", "MapPointType", "RewardType", "ChoicePromptKind", "CardType", "TargetType", "CardRarity", "RestOption", "CardRewardAlt", "ShopCategory" })
        {
            Assert.True(enums.GetProperty(e).GetArrayLength() > 0, e);
        }
        // 21 RunSim action kinds, then the 2 Vec-only rewards-screen macro kinds (RewardMacros)
        var kinds = enums.GetProperty("RunActionKind").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(23, kinds.Count);
        Assert.Equal(21, RewardMacros.FirstMacroKind);
        Assert.Equal("PromptCancel", kinds[20]);
        Assert.SequenceEqual(new[] { "TakeRewardCard", "TakeRewardAlternative" }, kinds.Skip(RewardMacros.FirstMacroKind));
        Assert.Equal(RewardMacros.KindCount, kinds.Count);
        Assert.Equal(11, enums.GetProperty("RunPhase").GetArrayLength());
        Assert.Equal(Caps.Candidates, root.GetProperty("caps").GetProperty("CANDIDATES").GetInt32());
        Assert.Equal(Caps.All.Count, root.GetProperty("caps").EnumerateObject().Count());
        Assert.True(root.GetProperty("card_vars").GetArrayLength() > 100, "card_vars");
        Assert.True(root.GetProperty("playable_reasons").GetArrayLength() > 1, "playable_reasons");
        Assert.Equal(Exports.Prefix + version, Exports.VersionString);
    }

    public static void Test_Vec_ExportsNeverThrow()
    {
        delegate* unmanaged<byte*, int, int> version = &Exports.Version;
        delegate* unmanaged<byte*, int, int> schema = &Exports.Schema;
        delegate* unmanaged<byte*, int, long> create = &Exports.Create;
        delegate* unmanaged<long, int> destroy = &Exports.Destroy;
        delegate* unmanaged<long, int, int> setThreads = &Exports.SetThreads;
        delegate* unmanaged<long, byte*, int, int> addFresh = &Exports.PoolAddFresh;
        delegate* unmanaged<long, byte*, int, int> addSave = &Exports.PoolAddSave;
        delegate* unmanaged<long, int> poolCount = &Exports.PoolCount;
        delegate* unmanaged<long, int, int> poolWarm = &Exports.PoolWarm;
        delegate* unmanaged<long, int, int> snapshot = &Exports.Snapshot;
        delegate* unmanaged<long, int, int, void**, int> restore = &Exports.Restore;
        delegate* unmanaged<long, byte*, int*, void**, int> reset = &Exports.Reset;
        delegate* unmanaged<long, int*, void**, int> step = &Exports.Step;
        delegate* unmanaged<long, int*, int, ulong*, int> stateHash = &Exports.StateHash;
        delegate* unmanaged<long, int, byte*, int, int> lastError = &Exports.LastError;

        string LastGlobal()
        {
            int n = lastError(0, -1, null, 0);
            var buf = new byte[Math.Max(1, n)];
            fixed (byte* p = buf)
            {
                lastError(0, -1, p, buf.Length);
            }
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        // version / schema: real, length-query protocol
        int vlen = version(null, 0);
        var vbuf = new byte[vlen];
        fixed (byte* p = vbuf)
        {
            Assert.Equal(vlen, version(p, vbuf.Length));
        }
        Assert.Equal(Exports.VersionString, Encoding.UTF8.GetString(vbuf));
        Assert.Equal(Encoding.UTF8.GetByteCount(Schema.ToJson()), schema(null, 0));

        // invalid handle → -1 + text, for every handle-taking export
        const long bad = 987654321;
        int one = 0;
        ulong h64 = 0;
        Assert.Equal(-1, destroy(bad));
        Assert.True(LastGlobal().Contains("invalid handle", StringComparison.Ordinal), LastGlobal());
        Assert.Equal(-1, setThreads(bad, 4));
        Assert.Equal(-1, addFresh(bad, null, 0));
        Assert.Equal(-1, addSave(bad, null, 0));
        Assert.Equal(-1, poolCount(bad));
        Assert.Equal(-1, poolWarm(bad, -1));
        Assert.Equal(-1, snapshot(bad, 0));
        Assert.Equal(-1, restore(bad, 0, 0, null));
        Assert.Equal(-1, reset(bad, null, null, null));
        Assert.Equal(-1, step(bad, &one, null));
        Assert.Equal(-1, stateHash(bad, &one, 1, &h64));
        Assert.Equal(-1, lastError(bad, -1, null, 0));
        Assert.True(LastGlobal().Contains("invalid handle", StringComparison.Ordinal), LastGlobal());

        // malformed config
        long Create(string cfg)
        {
            var bytes = Encoding.UTF8.GetBytes(cfg);
            fixed (byte* p = bytes)
            {
                return create(p, bytes.Length);
            }
        }

        bool saved = Exports.RequireServerGC;
        try
        {
            if (!GCSettings.IsServerGC)
            {
                Exports.RequireServerGC = true;
                Assert.Equal(-1L, Create("{\"num_envs\": 2}"));
                Assert.True(LastGlobal().Contains("Server GC", StringComparison.Ordinal), LastGlobal());
            }
            Exports.RequireServerGC = false;
            Assert.Equal(-1L, Create("{not json"));
            Assert.Equal(-1L, Create("{\"num_envs\": 2, \"bogus\": 1}"));
            Assert.True(LastGlobal().Contains("bogus", StringComparison.Ordinal), LastGlobal());
            Assert.Equal(-1L, Create("{\"threads\": 2}"));

            long h = Create("{\"num_envs\": 2, \"threads\": 2, \"pool\": {\"fresh\": {\"characters\": [\"IRONCLAD\"], \"count\": 2}}}");
            Assert.True(h > 0, LastGlobal());
            try
            {
                Assert.Equal(2, poolCount(h));
                Assert.Equal(-1, setThreads(h, 0));
                Assert.Equal(0, setThreads(h, 1));
                Assert.Equal(-1, snapshot(h, 0), "slot 0 holds no run yet");
                Assert.Equal(-1, snapshot(h, 7), "slot out of range");
                Assert.Equal(-1, lastError(h, 7, null, 0), "slot out of range");
                Assert.Equal(-1, reset(h, null, null, null), "null buffers");

                var badFresh = Encoding.UTF8.GetBytes("{\"character\": \"NOBODY\", \"seed\": \"S\"}");
                fixed (byte* p = badFresh)
                {
                    Assert.Equal(-1, addFresh(h, p, badFresh.Length));
                }
                var missing = Encoding.UTF8.GetBytes("/nonexistent/sts2vec.save");
                fixed (byte* p = missing)
                {
                    Assert.Equal(-1, addSave(h, p, missing.Length));
                }

                using var nb = new NativeBuffers(2);
                Assert.Equal(0, reset(h, null, null, nb.Table), LastGlobal());
                var st = nb.All<int>(Buf.Status);
                Assert.True(((SlotStatus)st[0]).HasFlag(SlotStatus.Reset), "reset sets RESET");
                int[] starts = { 5, 0 };
                fixed (int* s = starts)
                {
                    Assert.Equal(-1, reset(h, null, s, nb.Table), "start id beyond the pool");
                }
                Assert.Equal(-1, restore(h, 0, 99, nb.Table), "entry out of range");
                Assert.Equal(-1, restore(h, 9, 0, nb.Table), "slot out of range");
                int[] acts = { -1, 1_000_000 };
                fixed (int* a = acts)
                {
                    Assert.Equal(0, step(h, a, nb.Table), LastGlobal());
                }
                Assert.True(((SlotStatus)st[0]).HasFlag(SlotStatus.Noop), "action -1 → NOOP");
                Assert.True(((SlotStatus)st[1]).HasFlag(SlotStatus.IllegalAction), "index ≥ cand_n → ILLEGAL_ACTION");
                int[] slots = { 0, 5 };
                ulong[] hashes = new ulong[2];
                fixed (int* s = slots)
                fixed (ulong* o = hashes)
                {
                    Assert.Equal(-1, stateHash(h, s, 2, o), "slot 5 out of range");
                    Assert.Equal(0, stateHash(h, s, 1, o));
                }
                Assert.NotEqual(0UL, hashes[0]);
                int snap = snapshot(h, 0);
                Assert.Equal(2, snap);
                Assert.Equal(0, restore(h, 1, snap, nb.Table), LastGlobal());
            }
            finally
            {
                Assert.Equal(0, destroy(h));
            }
            Assert.Equal(-1, destroy(h), "double destroy");
        }
        finally
        {
            Exports.RequireServerGC = saved;
        }
    }

    public static void Test_Vec_ResetStepStatusFlow()
    {
        var env = new VecEnv(new VecConfig(3, Threads: 2, Pool: new FreshPoolSpec(new[] { "IRONCLAD", "SILENT" }, Count: 2)));
        using var nb = new NativeBuffers(3);
        env.Reset(default, default, nb.Table);
        var status = nb.All<int>(Buf.Status);
        var episode = nb.All<long>(Buf.Episode);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((int)SlotStatus.Reset, status[i] & ~(int)(SlotStatus.CandidateOverflow | SlotStatus.TableOverflow), $"slot {i}");
            Assert.Equal(i + 1L, episode[i * 3 + EpisodeCol.EpisodeId], "episode_id = slot + 1 for the first episode");
            Assert.Equal((long)env.Slots[i].StartEntry, episode[i * 3 + EpisodeCol.StartEntry]);
            Assert.Equal((int)((env.Slots[i].SlotSeed) % 2UL), env.Slots[i].StartEntry, "start sequence entry");
            Assert.NotNull(env.Slots[i].Sim);
        }
        Assert.Equal(VecEnv.SlotSeedOf("VEC", 1), env.Slots[1].SlotSeed);

        var actions = new int[] { -1, env.Slots[1].CandN, -1 };
        env.Step(actions, nb.Table);
        Assert.Equal((int)SlotStatus.Noop, status[0] & (int)SlotStatus.Noop);
        Assert.True(((SlotStatus)status[1]).HasFlag(SlotStatus.IllegalAction));

        // reset only slot 2 from entry 1: the episode id advances by N, the others' buffers are untouched.
        status[0] = 12345;
        env.Reset(new byte[] { 0, 0, 1 }, new[] { -1, -1, 1 }, nb.Table);
        Assert.Equal(12345, status[0]);
        Assert.Equal(2 + 1 + 3L, episode[2 * 3 + EpisodeCol.EpisodeId]);
        Assert.Equal(1L, episode[2 * 3 + EpisodeCol.StartEntry]);

        // empty pool → POOL_EMPTY
        var empty = new VecEnv(new VecConfig(1));
        using var nb1 = new NativeBuffers(1);
        empty.Reset(default, default, nb1.Table);
        Assert.True(((SlotStatus)nb1.All<int>(Buf.Status)[0]).HasFlag(SlotStatus.PoolEmpty));
        Assert.Null(empty.Slots[0].Sim);
    }

    public static void Test_Vec_ColumnIndicesMatchFields()
    {
        Assert.Equal(Schema.GI, Schema.Buffers[Buf.GlobalI].Shape[0]);
        Assert.Equal(0, GI.Phase);
        Assert.Equal(Schema.GI - 1, GI.IsCombat);
        Assert.Equal(Schema.GI - 2, GI.DecisionKind);
        Assert.Equal(22, Schema.HC);
        Assert.Equal(Schema.HandFields.Length - 1, HC.VarVal0 + Caps.HandVars - 1);
        Assert.Equal(HC.VarId0 + Caps.HandVars, HC.VarVal0);
        Assert.Equal(8, CandCol.TargetUid);
        Assert.Equal(7, LastEpCol.LastEncounterId);
        Assert.Equal(9, AuxCol.IsCombat);
        Assert.Equal(13, AuxCol.ActRooms);
        Assert.Equal(Buf.Count - 8, Buf.GlobalX);
        Assert.Equal(Buf.Count - 3, Buf.EventOptionValues);
        Assert.Equal(Buf.Count - 2, Buf.RewardChoices);
        Assert.Equal(Buf.Count - 1, Buf.MapRoutes);
        Assert.SequenceEqual(Schema.Buffers[Buf.MapNodes].Shape[..2], Schema.Buffers[Buf.MapRoutes].Shape[..2], "map_routes cells = map_nodes cells");
        Assert.Equal(3 + Schema.HC, Schema.Buffers[Buf.RewardChoices].Shape[1]);
        Assert.Equal(Caps.RewardChoices, Schema.Buffers[Buf.RewardChoices].Shape[0]);
        Assert.SequenceEqual(Schema.HandFields, Schema.RewardChoiceFields.Skip(RcCol.Card));
        Assert.Equal(Schema.HC, Schema.Buffers[Buf.Hand].Shape[1]);
        Assert.Equal(Caps.Hand, Schema.Buffers[Buf.HandX].Shape[0]);
        Assert.Equal(Caps.EventOptions, Schema.Buffers[Buf.EventOptionValues].Shape[0]);
        Assert.Equal(EvValCol.Value0 + Caps.EventValues, EvValCol.ItemN);
        Assert.Equal(Schema.EventOptionValueFields.Length, EvValCol.ItemKind0 + Caps.EventItems * EvValCol.ItemStride);
        foreach (int k in new[] { Buf.GlobalX, Buf.HandX, Buf.DrawMarked, Buf.DiscardOrder, Buf.ExhaustOrder, Buf.EventOptionValues, Buf.RewardChoices, Buf.MapRoutes })
        {
            Assert.True(Buf.Observation.Contains(k), $"{Schema.Buffers[k].Name} is an observation buffer");
        }
        foreach (var name in Exports.ExportNames)
        {
            var m = typeof(Exports).GetMethod(Exports.ManagedNameOf(name));
            Assert.NotNull(m, $"managed method for {name}");
            var attr = m!.GetCustomAttributes(typeof(System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute), false)
                .Cast<System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute>().Single();
            Assert.Equal(name, attr.EntryPoint);
        }
        Assert.Equal(16, Exports.ExportNames.Count);
        Assert.Equal("sts2vec_pool_warm", Exports.ExportNames[^2], "exports added after the scaffold freeze are appended");
        Assert.Equal("sts2vec_clone_determinized", Exports.ExportNames[^1], "exports added after the scaffold freeze are appended");
    }
}
