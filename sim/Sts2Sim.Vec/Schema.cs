using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Sts2Sim.Core.Random;

namespace Sts2Sim.Vec;

/// <summary>Table capacities (CONTRACTS.md §D3, CAP_*). Frozen. JSON keys under "caps" are the upper-case names.</summary>
public static class Caps
{
    public const int Hand = 10;
    public const int Deck = 96;
    public const int Relics = 32;
    public const int Potions = 10;
    public const int PlayerPowers = 24;
    public const int Enemies = 6;
    public const int EnemyIntents = 3;
    public const int EnemyPowers = 12;
    public const int Allies = 2;
    public const int AllyPowers = 8;
    public const int Orbs = 10;
    public const int MapRows = 18;
    public const int MapCols = 7;
    public const int Rewards = 8;
    public const int CardReward = 4;
    public const int CardRewardAlts = 2;
    public const int Shop = 14;
    public const int Rest = 9;
    public const int EventOptions = 8;
    public const int PromptOptions = 128;
    public const int Candidates = 192;
    public const int HandVars = 4;

    // Appended with the player-view schema append (docs/parity-audit-2026-11-02.md §5.2).
    /// <summary>Rows of <c>draw_marked</c>: draw-pile cards with an enchantment or an affliction (sorted, order hidden).</summary>
    public const int DrawMarked = 32;
    /// <summary>Rows of <c>discard_order</c> / <c>exhaust_order</c> (pile order).</summary>
    public const int PileOrder = 64;
    /// <summary>Numbers per row of <c>event_option_values</c>.</summary>
    public const int EventValues = 3;
    /// <summary>Items per row of <c>event_option_values</c>.</summary>
    public const int EventItems = 2;

    public static readonly IReadOnlyList<(string Name, int Value)> All = new[]
    {
        ("HAND", Hand), ("DECK", Deck), ("RELICS", Relics), ("POTIONS", Potions), ("PLAYER_POWERS", PlayerPowers),
        ("ENEMIES", Enemies), ("ENEMY_INTENTS", EnemyIntents), ("ENEMY_POWERS", EnemyPowers), ("ALLIES", Allies),
        ("ALLY_POWERS", AllyPowers), ("ORBS", Orbs), ("MAP_ROWS", MapRows), ("MAP_COLS", MapCols), ("REWARDS", Rewards),
        ("CARD_REWARD", CardReward), ("CARD_REWARD_ALTS", CardRewardAlts), ("SHOP", Shop), ("REST", Rest),
        ("EVENT_OPTIONS", EventOptions), ("PROMPT_OPTIONS", PromptOptions), ("CANDIDATES", Candidates), ("HAND_VARS", HandVars),
        ("DRAW_MARKED", DrawMarked), ("PILE_ORDER", PileOrder), ("EVENT_VALUES", EventValues), ("EVENT_ITEMS", EventItems),
    };
}

/// <summary>One per-env buffer: Python allocates (N, *Shape) of DType. Fields name the last axis (null for scalars / count vectors).</summary>
public sealed record BufferSpec(string Name, string DType, int[] Shape, string[]? Fields)
{
    public int ElementSize => Schema.SizeOf(DType);

    public int Elements => Shape.Aggregate(1, (a, b) => a * b);

    public int BytesPerEnv => Elements * ElementSize;
}

/// <summary>Buffer indices in <see cref="Schema.Buffers"/> order (= the order of the void** array). Frozen.</summary>
public static class Buf
{
    public const int GlobalI = 0;
    public const int GlobalF = 1;
    public const int Hand = 2;
    public const int HandF = 3;
    public const int HandDmg = 4;
    public const int Deck = 5;
    public const int DrawCounts = 6;
    public const int DiscardCounts = 7;
    public const int ExhaustCounts = 8;
    public const int Relics = 9;
    public const int Potions = 10;
    public const int PowersPlayer = 11;
    public const int Orbs = 12;
    public const int Enemies = 13;
    public const int EnemyIntents = 14;
    public const int EnemyPowers = 15;
    public const int Allies = 16;
    public const int AllyPowers = 17;
    public const int MapNodes = 18;
    public const int Rewards = 19;
    public const int CardReward = 20;
    public const int CardRewardAlts = 21;
    public const int Shop = 22;
    public const int Rest = 23;
    public const int EventOptions = 24;
    public const int PromptOptions = 25;
    public const int Cand = 26;
    public const int CandN = 27;
    public const int Reward = 28;
    public const int Terminated = 29;
    public const int Truncated = 30;
    public const int Status = 31;
    public const int Episode = 32;
    public const int Aux = 33;
    public const int LastEpisode = 34;
    // Appended after the signal buffers (player-view schema append, docs/parity-audit-2026-11-02.md §5.2); observation
    // buffers like 0-25 and aux.
    public const int GlobalX = 35;
    public const int HandX = 36;
    public const int DrawMarked = 37;
    public const int DiscardOrder = 38;
    public const int ExhaustOrder = 39;
    public const int EventOptionValues = 40;
    public const int Count = 41;

    /// <summary>Buffers the observation encoder owns (zero-filled + written per encode). The rest are env signals.</summary>
    public static readonly int[] Observation =
    {
        GlobalI, GlobalF, Hand, HandF, HandDmg, Deck, DrawCounts, DiscardCounts, ExhaustCounts, Relics, Potions,
        PowersPlayer, Orbs, Enemies, EnemyIntents, EnemyPowers, Allies, AllyPowers, MapNodes, Rewards, CardReward,
        CardRewardAlts, Shop, Rest, EventOptions, PromptOptions, Aux,
        GlobalX, HandX, DrawMarked, DiscardOrder, ExhaustOrder, EventOptionValues,
    };
}

/// <summary>The observation schema of CONTRACTS.md §D3 as code. Frozen (owners append fields only together with every caller).</summary>
public static class Schema
{
    public static readonly string[] GlobalIFields =
    {
        "phase", "character", "ascension", "act_index", "act_id", "total_floor", "act_floor", "map_row", "map_col",
        "hp", "max_hp", "gold", "energy", "max_energy", "stars", "block", "turn", "round", "potion_slots",
        "deck_n", "draw_n", "discard_n", "exhaust_n", "orb_slots", "relic_n", "hand_n", "enemy_n", "ally_n",
        "boss_id", "second_boss_id", "card_removals_used", "removal_price", "incoming_damage", "incoming_hits",
        "prompt_kind", "prompt_min", "prompt_max", "prompt_picked", "prompt_needs_confirm", "prompt_can_cancel",
        "prompt_can_skip", "prompt_ordered", "prompt_option_n", "prompt_pile", "prompt_site_hash",
        "reward_n", "card_reward_can_skip", "card_reward_skip_consumes", "treasure_opened", "treasure_relic_id",
        "treasure_claimed", "event_id", "event_finished", "event_option_n", "rest_option_n", "shop_entry_n",
        "decision_kind", "is_combat",
    };

    public static readonly string[] GlobalFFields = { "hp_frac", "gold_log1p", "floor_frac", "incoming_net_hp" };

    /// <summary>HC columns of <c>hand</c> and <c>card_reward</c> (card_reward leaves playable/playable_reason_id/hits at 0).</summary>
    public static readonly string[] HandFields =
    {
        "card_id", "upgrade", "cost_current", "cost_base", "type", "target", "rarity", "keyword_mask",
        "enchant_id", "enchant_amount", "affliction_id", "playable", "playable_reason_id", "hits",
        "var_id_0", "var_id_1", "var_id_2", "var_id_3", "var_val_0", "var_val_1", "var_val_2", "var_val_3",
    };

    public static readonly string[] HandFFields = { "block_eff", "damage_eff_self_or_all" };
    public static readonly string[] HandDmgFields = Enumerable.Range(0, Caps.Enemies).Select(j => $"vs_enemy_{j}").ToArray();
    public static readonly string[] DeckFields = { "card_id", "upgrade", "enchant_id", "enchant_amount", "affliction_id" };
    public static readonly string[] RelicFields = { "relic_id", "counter", "status", "used_up", "stack" };
    public static readonly string[] PotionFields = { "potion_id", "usable_now", "target_type" };
    public static readonly string[] PlayerPowerFields = { "power_id", "amount", "is_debuff" };
    public static readonly string[] OrbFields = { "orb_id", "passive", "evoke" };
    public static readonly string[] EnemyFields = { "present", "uid", "monster_id", "hp", "max_hp", "block", "flank", "behind", "intent_n" };
    public static readonly string[] IntentFields = { "type", "damage", "times", "card_count" };
    public static readonly string[] CreaturePowerFields = { "power_id", "amount" };
    public static readonly string[] AllyFields = { "present", "monster_id", "hp", "max_hp", "block" };
    public static readonly string[] MapNodeFields = { "point_type", "visited", "current", "legal_next", "child_mask", "quest" };
    public static readonly string[] RewardFields = { "reward_type", "amount", "item_id", "is_card_reward" };
    public static readonly string[] ShopFields = { "category", "item_id", "upgrade", "price", "stocked", "on_sale", "affordable" };
    public static readonly string[] RestFields = { "option_id", "enabled" };
    public static readonly string[] EventOptionFields = { "key_hash", "ordinal", "enabled", "locked", "proceed", "relic_id" };
    public static readonly string[] PromptOptionFields = { "item_id", "upgrade", "picked", "kind_of_item", "ordinal" };
    public static readonly string[] CandFields = { "kind", "A", "B", "hand_slot", "target_slot", "potion_slot", "option_index", "card_id", "target_uid" };
    public static readonly string[] EpisodeFields = { "episode_id", "step_index", "start_entry" };
    public static readonly string[] AuxFields = { "total_floor", "act_index", "hp", "max_hp", "gold", "bosses_killed", "combats_won", "deck_n", "relic_n", "is_combat",
        "act_floor", "map_row", "map_col", "act_rooms" };
    public static readonly string[] LastEpisodeFields = { "win", "floor", "act", "steps", "final_hp", "final_max_hp", "end_kind", "last_encounter_id" };

    // ---- appended buffers (player-view schema append) ----

    /// <summary><c>global_x</c>: run / combat scalars appended after the freeze.</summary>
    public static readonly string[] GlobalXFields = { "ancient_id", "draw_marked_n" };

    /// <summary><c>hand_x</c>: per hand card, the columns appended after the freeze (row i = <c>hand</c> row i).</summary>
    public static readonly string[] HandXFields = { "affliction_amount", "dampened" };

    /// <summary>A combat-pile card with its marks: <c>draw_marked</c>, <c>discard_order</c>, <c>exhaust_order</c>.</summary>
    public static readonly string[] PileCardFields = { "card_id", "upgrade", "enchant_id", "enchant_amount", "affliction_id", "affliction_amount" };

    /// <summary><c>event_option_values</c> (row i = <c>event_options</c> row i).</summary>
    public static readonly string[] EventOptionValueFields =
        { "value_n", "value_0", "value_1", "value_2", "item_n", "item_kind_0", "item_id_0", "item_kind_1", "item_id_1" };

    public static int GI => GlobalIFields.Length;
    public static int GF => GlobalFFields.Length;
    public static int HC => HandFields.Length;
    public static int A => AuxFields.Length;

    /// <summary>V_cards = vocab.cards.Count; the count vectors have shape [V_cards * 2] (index card_id * 2 + upgrade).</summary>
    public static int VCards => Vocab.Cards.Count;

    private static readonly Lazy<BufferSpec[]> BuffersL = new(BuildBuffers);
    private static readonly Lazy<int[]> BytesL = new(() => BuffersL.Value.Select(b => b.BytesPerEnv).ToArray());
    private static readonly Lazy<int[]> ElementsL = new(() => BuffersL.Value.Select(b => b.Elements).ToArray());
    private static readonly Lazy<string> JsonL = new(BuildJson);
    private static readonly Lazy<string> VersionL = new(() => ComputeVersion(BuildBody()));

    public static IReadOnlyList<BufferSpec> Buffers => BuffersL.Value;

    public static int BytesPerEnv(int index) => BytesL.Value[index];

    public static int Elements(int index) => ElementsL.Value[index];

    public static int BytesPerEnv(string name) => BytesPerEnv(IndexOf(name));

    public static int Elements(string name) => Elements(IndexOf(name));

    public static int IndexOf(string name)
    {
        var b = BuffersL.Value;
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i].Name == name)
            {
                return i;
            }
        }
        throw new KeyNotFoundException($"no buffer named '{name}'");
    }

    /// <summary>16 lower-case hex digits: XXH64 (seed 0) of the UTF-8 schema JSON body without the encoder_version member.</summary>
    public static string EncoderVersion => VersionL.Value;

    /// <summary>The §D3.2 schema JSON (published by sts2vec_schema).</summary>
    public static string ToJson() => JsonL.Value;

    public static int SizeOf(string dtype) => dtype switch
    {
        "int32" or "float32" => 4,
        "int16" => 2,
        "int64" => 8,
        "uint8" => 1,
        _ => throw new ArgumentException($"unknown dtype {dtype}"),
    };

    /// <summary>Column index of <paramref name="name"/> in <paramref name="fields"/>; throws if absent (used by the column classes).</summary>
    public static int Col(string[] fields, string name)
    {
        int i = Array.IndexOf(fields, name);
        return i >= 0 ? i : throw new InvalidOperationException($"schema field '{name}' missing");
    }

    private static BufferSpec[] BuildBuffers()
    {
        int vc2 = VCards * 2;
        var list = new[]
        {
            new BufferSpec("global_i", "int32", new[] { GI }, GlobalIFields),
            new BufferSpec("global_f", "float32", new[] { GF }, GlobalFFields),
            new BufferSpec("hand", "int32", new[] { Caps.Hand, HC }, HandFields),
            new BufferSpec("hand_f", "float32", new[] { Caps.Hand, HandFFields.Length }, HandFFields),
            new BufferSpec("hand_dmg", "float32", new[] { Caps.Hand, Caps.Enemies }, HandDmgFields),
            new BufferSpec("deck", "int32", new[] { Caps.Deck, DeckFields.Length }, DeckFields),
            new BufferSpec("draw_counts", "int16", new[] { vc2 }, null),
            new BufferSpec("discard_counts", "int16", new[] { vc2 }, null),
            new BufferSpec("exhaust_counts", "int16", new[] { vc2 }, null),
            new BufferSpec("relics", "int32", new[] { Caps.Relics, RelicFields.Length }, RelicFields),
            new BufferSpec("potions", "int32", new[] { Caps.Potions, PotionFields.Length }, PotionFields),
            new BufferSpec("powers_player", "int32", new[] { Caps.PlayerPowers, PlayerPowerFields.Length }, PlayerPowerFields),
            new BufferSpec("orbs", "int32", new[] { Caps.Orbs, OrbFields.Length }, OrbFields),
            new BufferSpec("enemies", "int32", new[] { Caps.Enemies, EnemyFields.Length }, EnemyFields),
            new BufferSpec("enemy_intents", "int32", new[] { Caps.Enemies, Caps.EnemyIntents, IntentFields.Length }, IntentFields),
            new BufferSpec("enemy_powers", "int32", new[] { Caps.Enemies, Caps.EnemyPowers, CreaturePowerFields.Length }, CreaturePowerFields),
            new BufferSpec("allies", "int32", new[] { Caps.Allies, AllyFields.Length }, AllyFields),
            new BufferSpec("ally_powers", "int32", new[] { Caps.Allies, Caps.AllyPowers, CreaturePowerFields.Length }, CreaturePowerFields),
            new BufferSpec("map_nodes", "int32", new[] { Caps.MapRows, Caps.MapCols, MapNodeFields.Length }, MapNodeFields),
            new BufferSpec("rewards", "int32", new[] { Caps.Rewards, RewardFields.Length }, RewardFields),
            new BufferSpec("card_reward", "int32", new[] { Caps.CardReward, HC }, HandFields),
            new BufferSpec("card_reward_alts", "int32", new[] { Caps.CardRewardAlts }, null),
            new BufferSpec("shop", "int32", new[] { Caps.Shop, ShopFields.Length }, ShopFields),
            new BufferSpec("rest", "int32", new[] { Caps.Rest, RestFields.Length }, RestFields),
            new BufferSpec("event_options", "int32", new[] { Caps.EventOptions, EventOptionFields.Length }, EventOptionFields),
            new BufferSpec("prompt_options", "int32", new[] { Caps.PromptOptions, PromptOptionFields.Length }, PromptOptionFields),
            new BufferSpec("cand", "int32", new[] { Caps.Candidates, CandFields.Length }, CandFields),
            new BufferSpec("cand_n", "int32", Array.Empty<int>(), null),
            new BufferSpec("reward", "float32", Array.Empty<int>(), null),
            new BufferSpec("terminated", "uint8", Array.Empty<int>(), null),
            new BufferSpec("truncated", "uint8", Array.Empty<int>(), null),
            new BufferSpec("status", "int32", Array.Empty<int>(), null),
            new BufferSpec("episode", "int64", new[] { EpisodeFields.Length }, EpisodeFields),
            new BufferSpec("aux", "float32", new[] { A }, AuxFields),
            new BufferSpec("last_episode", "int32", new[] { LastEpisodeFields.Length }, LastEpisodeFields),
            new BufferSpec("global_x", "int32", new[] { GlobalXFields.Length }, GlobalXFields),
            new BufferSpec("hand_x", "int32", new[] { Caps.Hand, HandXFields.Length }, HandXFields),
            new BufferSpec("draw_marked", "int32", new[] { Caps.DrawMarked, PileCardFields.Length }, PileCardFields),
            new BufferSpec("discard_order", "int32", new[] { Caps.PileOrder, PileCardFields.Length }, PileCardFields),
            new BufferSpec("exhaust_order", "int32", new[] { Caps.PileOrder, PileCardFields.Length }, PileCardFields),
            new BufferSpec("event_option_values", "int32", new[] { Caps.EventOptions, EventOptionValueFields.Length }, EventOptionValueFields),
        };
        if (list.Length != Buf.Count)
        {
            throw new InvalidOperationException($"schema has {list.Length} buffers, Buf.Count = {Buf.Count}");
        }
        return list;
    }

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>The schema JSON without the encoder_version member: <c>{"buffers":…,"vocab":…,"caps":…,"card_vars":…,"playable_reasons":…,"playable_reason_bits":…}</c>.</summary>
    internal static string BuildBody()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteStartArray("buffers");
            foreach (var b in Buffers)
            {
                w.WriteStartObject();
                w.WriteString("name", b.Name);
                w.WriteString("dtype", b.DType);
                w.WriteStartArray("shape");
                foreach (int d in b.Shape)
                {
                    w.WriteNumberValue(d);
                }
                w.WriteEndArray();
                if (b.Fields == null)
                {
                    w.WriteNull("fields");
                }
                else
                {
                    WriteStrings(w, "fields", b.Fields);
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("vocab");
            foreach (var v in Vocab.Lists)
            {
                WriteStrings(w, v.Kind, v.Names);
            }
            w.WriteStartObject("enums");
            foreach (var (name, values) in Vocab.Enums)
            {
                WriteStrings(w, name, values);
            }
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("caps");
            foreach (var (name, value) in Caps.All)
            {
                w.WriteNumber(name, value);
            }
            w.WriteEndObject();

            WriteStrings(w, "card_vars", Vocab.CardVars.Names);
            WriteStrings(w, "playable_reasons", Vocab.PlayableReasons);
            w.WriteStartObject("playable_reason_bits");
            foreach (var (name, bit) in Vocab.PlayableReasonBits)
            {
                w.WriteNumber(name, bit);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static string ComputeVersion(string body) =>
        DeterministicHash.XxHash64(Encoding.UTF8.GetBytes(body)).ToString("x16");

    private static string BuildJson()
    {
        string body = BuildBody();
        string version = ComputeVersion(body);
        // body starts with '{' and has at least one member: splice the version in as the first member.
        return "{\"encoder_version\":\"" + version + "\"," + body.Substring(1);
    }

    private static void WriteStrings(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WriteStartArray(name);
        foreach (var s in values)
        {
            w.WriteStringValue(s);
        }
        w.WriteEndArray();
    }
}

// ---- Column indices (resolved from the field lists, so they can never drift from the published schema) ----

/// <summary>global_i columns.</summary>
public static class GI
{
    private static int C(string n) => Schema.Col(Schema.GlobalIFields, n);
    public static readonly int Phase = C("phase"), Character = C("character"), Ascension = C("ascension"),
        ActIndex = C("act_index"), ActId = C("act_id"), TotalFloor = C("total_floor"), ActFloor = C("act_floor"),
        MapRow = C("map_row"), MapCol = C("map_col"), Hp = C("hp"), MaxHp = C("max_hp"), Gold = C("gold"),
        Energy = C("energy"), MaxEnergy = C("max_energy"), Stars = C("stars"), Block = C("block"), Turn = C("turn"),
        Round = C("round"), PotionSlots = C("potion_slots"), DeckN = C("deck_n"), DrawN = C("draw_n"),
        DiscardN = C("discard_n"), ExhaustN = C("exhaust_n"), OrbSlots = C("orb_slots"), RelicN = C("relic_n"),
        HandN = C("hand_n"), EnemyN = C("enemy_n"), AllyN = C("ally_n"), BossId = C("boss_id"),
        SecondBossId = C("second_boss_id"), CardRemovalsUsed = C("card_removals_used"), RemovalPrice = C("removal_price"),
        IncomingDamage = C("incoming_damage"), IncomingHits = C("incoming_hits"), PromptKind = C("prompt_kind"),
        PromptMin = C("prompt_min"), PromptMax = C("prompt_max"), PromptPicked = C("prompt_picked"),
        PromptNeedsConfirm = C("prompt_needs_confirm"), PromptCanCancel = C("prompt_can_cancel"),
        PromptCanSkip = C("prompt_can_skip"), PromptOrdered = C("prompt_ordered"), PromptOptionN = C("prompt_option_n"),
        PromptPile = C("prompt_pile"), PromptSiteHash = C("prompt_site_hash"), RewardN = C("reward_n"),
        CardRewardCanSkip = C("card_reward_can_skip"), CardRewardSkipConsumes = C("card_reward_skip_consumes"),
        TreasureOpened = C("treasure_opened"), TreasureRelicId = C("treasure_relic_id"),
        TreasureClaimed = C("treasure_claimed"), EventId = C("event_id"), EventFinished = C("event_finished"),
        EventOptionN = C("event_option_n"), RestOptionN = C("rest_option_n"), ShopEntryN = C("shop_entry_n"),
        DecisionKind = C("decision_kind"), IsCombat = C("is_combat");
}

/// <summary>global_f columns.</summary>
public static class GF
{
    private static int C(string n) => Schema.Col(Schema.GlobalFFields, n);
    public static readonly int HpFrac = C("hp_frac"), GoldLog1p = C("gold_log1p"), FloorFrac = C("floor_frac"),
        IncomingNetHp = C("incoming_net_hp");
}

/// <summary>hand / card_reward columns (HC).</summary>
public static class HC
{
    private static int C(string n) => Schema.Col(Schema.HandFields, n);
    public static readonly int CardId = C("card_id"), Upgrade = C("upgrade"), CostCurrent = C("cost_current"),
        CostBase = C("cost_base"), Type = C("type"), Target = C("target"), Rarity = C("rarity"),
        KeywordMask = C("keyword_mask"), EnchantId = C("enchant_id"), EnchantAmount = C("enchant_amount"),
        AfflictionId = C("affliction_id"), Playable = C("playable"), PlayableReasonId = C("playable_reason_id"),
        Hits = C("hits"), VarId0 = C("var_id_0"), VarVal0 = C("var_val_0");

    /// <summary>cost_current sentinel for X-cost cards.</summary>
    public const int CostX = -1;

    /// <summary>cost_current sentinel for unplayable-cost cards.</summary>
    public const int CostUnplayable = -2;
}

/// <summary>cand columns.</summary>
public static class CandCol
{
    private static int C(string n) => Schema.Col(Schema.CandFields, n);
    public static readonly int Kind = C("kind"), A = C("A"), B = C("B"), HandSlot = C("hand_slot"),
        TargetSlot = C("target_slot"), PotionSlot = C("potion_slot"), OptionIndex = C("option_index"),
        CardId = C("card_id"), TargetUid = C("target_uid");
}

/// <summary>episode columns (int64).</summary>
public static class EpisodeCol
{
    private static int C(string n) => Schema.Col(Schema.EpisodeFields, n);
    public static readonly int EpisodeId = C("episode_id"), StepIndex = C("step_index"), StartEntry = C("start_entry");
}

/// <summary>aux columns (float32).</summary>
public static class AuxCol
{
    private static int C(string n) => Schema.Col(Schema.AuxFields, n);
    public static readonly int TotalFloor = C("total_floor"), ActIndex = C("act_index"), Hp = C("hp"), MaxHp = C("max_hp"),
        Gold = C("gold"), BossesKilled = C("bosses_killed"), CombatsWon = C("combats_won"), DeckN = C("deck_n"),
        RelicN = C("relic_n"), IsCombat = C("is_combat"), ActFloor = C("act_floor"), MapRow = C("map_row"),
        MapCol = C("map_col"), ActRooms = C("act_rooms");
}

/// <summary>last_episode columns (int32).</summary>
public static class LastEpCol
{
    private static int C(string n) => Schema.Col(Schema.LastEpisodeFields, n);
    public static readonly int Win = C("win"), Floor = C("floor"), Act = C("act"), Steps = C("steps"), FinalHp = C("final_hp"),
        FinalMaxHp = C("final_max_hp"), EndKind = C("end_kind"), LastEncounterId = C("last_encounter_id");
}

/// <summary>global_x columns (appended).</summary>
public static class GX
{
    private static int C(string n) => Schema.Col(Schema.GlobalXFields, n);
    public static readonly int AncientId = C("ancient_id"), DrawMarkedN = C("draw_marked_n");
}

/// <summary>hand_x columns (appended).</summary>
public static class HX
{
    private static int C(string n) => Schema.Col(Schema.HandXFields, n);
    public static readonly int AfflictionAmount = C("affliction_amount"), Dampened = C("dampened");
}

/// <summary>draw_marked / discard_order / exhaust_order columns (appended).</summary>
public static class PileCol
{
    private static int C(string n) => Schema.Col(Schema.PileCardFields, n);
    public static readonly int CardId = C("card_id"), Upgrade = C("upgrade"), EnchantId = C("enchant_id"),
        EnchantAmount = C("enchant_amount"), AfflictionId = C("affliction_id"), AfflictionAmount = C("affliction_amount");
}

/// <summary>event_option_values columns (appended).</summary>
public static class EvValCol
{
    private static int C(string n) => Schema.Col(Schema.EventOptionValueFields, n);
    public static readonly int ValueN = C("value_n"), Value0 = C("value_0"), ItemN = C("item_n"),
        ItemKind0 = C("item_kind_0"), ItemId0 = C("item_id_0");

    /// <summary>Columns per item (item_kind_k, item_id_k).</summary>
    public const int ItemStride = 2;
}
