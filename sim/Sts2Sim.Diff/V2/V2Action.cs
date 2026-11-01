namespace Sts2Sim.Diff.V2;

public readonly record struct ChoosePick(int? Index, int? DeckIndex, int? CombatCardIndex, string? CardId, bool? Upgraded);

public sealed class V2Action
{
    public const string PlayCard = "play_card";
    public const string EndTurn = "end_turn";
    public const string UndoEndTurn = "undo_end_turn";
    public const string UsePotion = "use_potion";
    public const string DiscardPotion = "discard_potion";
    public const string Choose = "choose";
    public const string MapMove = "map_move";
    public const string NextAct = "next_act";
    public const string EventOption = "event_option";
    public const string EventProceed = "event_proceed";
    public const string RewardTake = "reward_take";
    public const string RewardsSkip = "rewards_skip";
    public const string CardRewardPick = "card_reward_pick";
    public const string CardRewardSkip = "card_reward_skip";
    public const string TreasureOpen = "treasure_open";
    public const string TreasurePick = "treasure_pick";
    public const string ShopBuy = "shop_buy";
    public const string ShopRemove = "shop_remove";
    public const string ShopLeave = "shop_leave";
    public const string RestOption = "rest_option";
    public const string RestLeave = "rest_leave";
    public const string RelicSelect = "relic_select";

    public static readonly IReadOnlySet<string> CombatTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        PlayCard, EndTurn, UndoEndTurn, UsePotion, DiscardPotion, Choose
    };

    public static readonly IReadOnlySet<string> AllTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        PlayCard, EndTurn, UndoEndTurn, UsePotion, DiscardPotion, Choose, MapMove, NextAct, EventOption, EventProceed,
        RewardTake, RewardsSkip, CardRewardPick, CardRewardSkip, TreasureOpen, TreasurePick, ShopBuy, ShopRemove, ShopLeave,
        RestOption, RestLeave, RelicSelect
    };

    private static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        [PlayCard] = new[] { "hand_index", "card_id", "upgraded", "target_combat_id" },
        [EndTurn] = Array.Empty<string>(),
        [UndoEndTurn] = Array.Empty<string>(),
        [UsePotion] = new[] { "slot", "potion_id", "target_combat_id", "in_combat" },
        [DiscardPotion] = new[] { "slot", "potion_id", "in_combat" },
        [Choose] = new[] { "context", "kind", "picks", "skipped", "confirm" },
        [MapMove] = new[] { "col", "row" },
        [NextAct] = new[] { "col", "row" },
        [EventOption] = new[] { "text_key", "index" },
        [EventProceed] = new[] { "text_key", "index" },
        [RewardTake] = new[] { "set_index", "index", "reward_type", "item_id" },
        [RewardsSkip] = Array.Empty<string>(),
        [CardRewardPick] = new[] { "index", "card_id", "upgraded" },
        [CardRewardSkip] = new[] { "index", "card_id" },
        [TreasureOpen] = Array.Empty<string>(),
        [TreasurePick] = new[] { "index", "relic_id", "skipped" },
        [ShopBuy] = new[] { "category", "item_id", "price", "slot" },
        [ShopRemove] = new[] { "category", "item_id", "price", "slot" },
        [ShopLeave] = Array.Empty<string>(),
        [RestOption] = new[] { "option_id" },
        [RestLeave] = Array.Empty<string>(),
        [RelicSelect] = new[] { "relic_id", "index" }
    };

    public V2Action(string type, JsonObject raw, IReadOnlyList<ChoosePick>? picks = null)
    {
        Type = type;
        Raw = raw;
        Picks = picks ?? Array.Empty<ChoosePick>();
    }

    public string Type { get; }

    public JsonObject Raw { get; }

    public IReadOnlyList<ChoosePick> Picks { get; }

    public int? Int(string name) => Raw[name] is JsonValue v && v.TryGetValue(out int i) ? i : null;

    public string? Str(string name) => Raw[name] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    public bool? Bool(string name) => Raw[name] is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    public JsonObject ToJson() => (JsonObject)Raw.DeepClone();

    public override string ToString() => Raw.ToJsonString();

    public static V2Action FromV1(BundleAction a) => new(a.Type == BundleActionType.EndTurn ? EndTurn : PlayCard, a.ToJson());

    public BundleAction? ToV1()
    {
        if (Type == EndTurn)
        {
            return BundleAction.EndTurn;
        }
        if (Type == PlayCard && Int("hand_index") is int hand)
        {
            return BundleAction.Play(hand, Str("card_id"), Bool("upgraded"), Int("target_combat_id"));
        }
        return null;
    }

    internal static V2Action Read(JsonReadHelper r, JsonElement a, string path)
    {
        r.RequireObject(a, path);
        string type = r.String(a, "type", path);
        if (!Fields.TryGetValue(type, out var fields))
        {
            throw new BundleFormatException(path + ".type", $"unsupported action type '{type}'");
        }
        var known = new string[fields.Length + 1];
        known[0] = "type";
        fields.CopyTo(known, 1);
        r.NoteUnknownKeys(a, path, known);
        var raw = JsonNode.Parse(a.GetRawText())!.AsObject();
        List<ChoosePick>? picks = null;
        switch (type)
        {
            case PlayCard:
                int hand = r.Int(a, "hand_index", path);
                if (hand < 0)
                {
                    throw new BundleFormatException(path + ".hand_index", $"must be non-negative, got {hand}");
                }
                break;
            case UsePotion:
            case DiscardPotion:
                if (r.Int(a, "slot", path) < 0)
                {
                    throw new BundleFormatException(path + ".slot", "must be non-negative");
                }
                break;
            case MapMove:
                r.Int(a, "col", path);
                r.Int(a, "row", path);
                break;
            case RestOption:
                r.String(a, "option_id", path);
                break;
            case Choose:
                picks = new List<ChoosePick>();
                if (r.TryProperty(a, "picks", out var picksEl))
                {
                    r.RequireArray(picksEl, path + ".picks");
                    int i = 0;
                    foreach (var p in picksEl.EnumerateArray())
                    {
                        string pp = $"{path}.picks[{i}]";
                        r.RequireObject(p, pp);
                        r.NoteUnknownKeys(p, pp, "index", "deck_index", "combat_card_index", "card_id", "upgraded");
                        bool? up = r.TryProperty(p, "upgraded", out _) ? r.Bool(p, "upgraded", pp) : null;
                        var pick = new ChoosePick(r.OptionalInt(p, "index", pp), r.OptionalInt(p, "deck_index", pp), r.OptionalInt(p, "combat_card_index", pp), r.OptionalString(p, "card_id", pp), up);
                        if (pick.Index == null && pick.CardId == null)
                        {
                            throw new BundleFormatException(pp, "a pick needs 'index' or 'card_id'");
                        }
                        picks.Add(pick);
                        i++;
                    }
                }
                break;
        }
        return new V2Action(type, raw, picks);
    }
}
