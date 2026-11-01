using System.Globalization;
using System.Text.RegularExpressions;

namespace Sts2Sim.Server;

public readonly record struct LegalEntry(string Token, string Desc, RunAction Action);

// Token <-> action mapping. Tokens are produced from the legal action list, so parsing is a lookup:
// a token is legal iff it (after normalisation and aliasing) names one listed entry.
public static partial class ActionTokens
{
    private static readonly string[] ExitTokens = { "proceed", "leave" };

    public static List<LegalEntry> Enumerate(GameSession g)
    {
        var actions = g.LegalActions();
        var entries = new List<LegalEntry>(actions.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool prompt = g.PendingChoice != null;
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            string token = Token(g, a, prompt);
            if (!seen.Add(token))
            {
                token = Raw(a);
                if (!seen.Add(token))
                {
                    token = Raw(a) + " #" + i.ToString(CultureInfo.InvariantCulture);
                    seen.Add(token);
                }
            }
            string desc;
            try
            {
                desc = Desc(g, a, prompt);
            }
            catch (Exception ex)
            {
                desc = a + " (" + ex.GetType().Name + ")";
            }
            entries.Add(new LegalEntry(token, desc, a));
        }
        return entries;
    }

    public static string Normalize(string raw)
    {
        string s = (raw ?? string.Empty).Trim().ToLowerInvariant();
        s = s.Replace("->", " -> ", StringComparison.Ordinal);
        s = Spaces().Replace(s, " ").Trim();
        if (s is "end turn" or "end_turn" or "endturn")
        {
            s = "end";
        }
        var m = TwoNumbers().Match(s);
        if (m.Success)
        {
            s = $"{m.Groups[1].Value} {m.Groups[2].Value} -> {m.Groups[3].Value}";
        }
        return s;
    }

    public static bool TryResolve(IReadOnlyList<LegalEntry> legal, string raw, out LegalEntry entry, out string error)
    {
        string token = Normalize(raw);
        for (int i = 0; i < legal.Count; i++)
        {
            if (legal[i].Token == token)
            {
                entry = legal[i];
                error = string.Empty;
                return true;
            }
        }
        string? alias = token switch
        {
            "take" => "take 0",
            "next" or "proceed" or "leave" or "continue" => FindExit(legal),
            _ => null
        };
        if (alias != null)
        {
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Token == alias)
                {
                    entry = legal[i];
                    error = string.Empty;
                    return true;
                }
            }
        }
        entry = default;
        error = legal.Count == 0
            ? $"'{raw}' is not legal: the game has no legal actions (it is over)."
            : $"'{raw}' is not a legal token here (normalised '{token}'); pick one of legal_actions.";
        return false;
    }

    private static string? FindExit(IReadOnlyList<LegalEntry> legal)
    {
        for (int i = 0; i < legal.Count; i++)
        {
            if (Array.IndexOf(ExitTokens, legal[i].Token) >= 0)
            {
                return legal[i].Token;
            }
        }
        return null;
    }

    public static string Raw(RunAction a)
    {
        string kind = Describe.Snake(a.Kind.ToString());
        if (a.A == RunAction.NoIndex && a.B == RunAction.NoIndex)
        {
            return kind;
        }
        return a.B == RunAction.NoIndex
            ? $"{kind} {a.A.ToString(CultureInfo.InvariantCulture)}"
            : $"{kind} {a.A.ToString(CultureInfo.InvariantCulture)} {a.B.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static string Token(GameSession g, RunAction a, bool prompt)
    {
        switch (a.Kind)
        {
            case RunActionKind.PlayCard:
                return a.B == RunAction.NoIndex ? $"play {I(a.A)}" : $"play {I(a.A)} -> {I(a.B)}";
            case RunActionKind.EndTurn:
                return "end";
            case RunActionKind.UsePotion:
                if (a.B == RunAction.NoIndex)
                {
                    return $"potion {I(a.A)}";
                }
                return a.B >= 0
                    ? $"potion {I(a.A)} -> {I(a.B)}"
                    : $"potion {I(a.A)} -> ally {I(LegalActionGenerator.PlayerAllyTargetIndex - a.B)}";
            case RunActionKind.DiscardPotion:
                return $"discard {I(a.A)}";
            case RunActionKind.PromptSelect:
                return $"pick {I(a.A)}";
            case RunActionKind.PromptConfirm:
                return "confirm";
            case RunActionKind.PromptCancel:
                return "cancel";
            case RunActionKind.Travel:
                return $"travel {I(a.A)}";
            case RunActionKind.ClaimReward:
                return $"reward {I(a.A)}";
            case RunActionKind.ProceedRewards:
                return "proceed";
            case RunActionKind.PickRewardCard:
                return $"card {I(a.A)}";
            case RunActionKind.RewardAlternative:
            {
                var alt = Alternative(g, a.A);
                if (alt != null && alt.OptionId == CardRewardAlternative.SkipId)
                {
                    return "skip";
                }
                if (alt != null && alt.OptionId == CardRewardAlternative.RerollId)
                {
                    return "reroll";
                }
                return $"alt {I(a.A)}";
            }
            case RunActionKind.BuyEntry:
                return ShopEntry(g, a.A) is MerchantCardRemovalEntry ? "remove" : $"buy {I(a.A)}";
            case RunActionKind.LeaveShop:
            case RunActionKind.LeaveRestSite:
            case RunActionKind.LeaveEvent:
                return "leave";
            case RunActionKind.ChooseRestOption:
                return $"rest {I(a.A)}";
            case RunActionKind.OpenChest:
                return "open";
            case RunActionKind.TakeTreasureRelic:
                return "take 0";
            case RunActionKind.SkipTreasureRelic:
                return "skip";
            case RunActionKind.ChooseEventOption:
                return $"event {I(a.A)}";
            default:
                return Raw(a);
        }
    }

    public static CardRewardAlternative? Alternative(GameSession g, int index)
    {
        if (g.Run == null)
        {
            return null;
        }
        var reward = RunLegalActionGenerator.OpenCardReward(g.Run.State, g.Run.Loop);
        return reward != null && index >= 0 && index < reward.Alternatives.Count ? reward.Alternatives[index] : null;
    }

    public static MerchantEntry? ShopEntry(GameSession g, int index)
    {
        if (g.Run == null || RoomStack.CurrentRoom(g.Run.State) is not MerchantRoom { Inventory: { } inventory })
        {
            return null;
        }
        var all = inventory.AllEntries;
        return index >= 0 && index < all.Count ? all[index] : null;
    }

    public static string? ShopEntryId(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry c => c.CreationResult is { } r ? Describe.CardName(r.Card) : null,
        MerchantRelicEntry r => r.Model?.Id,
        MerchantPotionEntry p => p.Model?.Id,
        MerchantCardRemovalEntry => "CARD_REMOVAL",
        _ => null
    };

    public static string ShopCategory(MerchantInventory inventory, int index, MerchantEntry entry)
    {
        switch (entry)
        {
            case MerchantCardEntry:
                return index < inventory.CharacterCardEntries.Count ? "card" : "colorless_card";
            case MerchantRelicEntry:
                return "relic";
            case MerchantPotionEntry:
                return "potion";
            case MerchantCardRemovalEntry:
                return "card_removal";
            default:
                return "other";
        }
    }

    private static int SafeCost(RunState rs, MerchantEntry entry)
    {
        try
        {
            return entry.GetCost(rs);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public static string Desc(GameSession g, RunAction a, bool prompt)
    {
        var cs = g.LiveCombat;
        var rs = g.Run?.State;
        switch (a.Kind)
        {
            case RunActionKind.PlayCard:
            {
                if (cs == null || a.A < 0 || a.A >= cs.Player.Hand.Count)
                {
                    return "play card " + I(a.A);
                }
                var card = cs.Player.Hand[a.A];
                string vars = Describe.VarSummary(card);
                string head = $"play {Describe.CardName(card)} [{Describe.CostText(cs, card)}]" + (vars.Length > 0 ? $" ({vars})" : string.Empty);
                return a.B == RunAction.NoIndex ? head : $"{head} on enemy {I(a.B)} {Describe.MonsterName(cs, a.B)}";
            }
            case RunActionKind.EndTurn:
                return "end the turn";
            case RunActionKind.UsePotion:
            {
                var potion = PotionAt(g.Player, a.A);
                string head = $"use potion {potion}";
                if (a.B == RunAction.NoIndex)
                {
                    return head;
                }
                if (a.B >= 0)
                {
                    return cs != null ? $"{head} on enemy {I(a.B)} {Describe.MonsterName(cs, a.B)}" : $"{head} on {I(a.B)}";
                }
                int ally = LegalActionGenerator.PlayerAllyTargetIndex - a.B;
                string allyName = cs != null && ally < cs.Allies.Count && cs.Allies[ally] is MonsterState pet ? pet.MonsterId : "ally";
                return $"{head} on ally {I(ally)} {allyName}";
            }
            case RunActionKind.DiscardPotion:
                return $"discard potion {PotionAt(g.Player, a.A)}";
            case RunActionKind.PromptSelect:
            {
                var p = g.PendingChoice;
                if (p != null && a.A >= 0 && a.A < p.Options.Count)
                {
                    return $"select {Describe.OptionName(p.Options[a.A])} ({p.Kind})";
                }
                return "select option " + I(a.A);
            }
            case RunActionKind.PromptConfirm:
                return $"confirm the selection ({g.PendingPicks.Count} picked)";
            case RunActionKind.PromptCancel:
                return "cancel the selection";
            case RunActionKind.Travel:
            {
                if (rs == null)
                {
                    return "travel " + I(a.A);
                }
                var points = new List<MapPoint>();
                RunLegalActionGenerator.GetLegalMapPoints(rs, points);
                if (a.A < 0 || a.A >= points.Count)
                {
                    return "travel " + I(a.A);
                }
                var pt = points[a.A];
                return $"travel to {pt.PointType} (col {I(pt.coord.col)}, row {I(pt.coord.row)})";
            }
            case RunActionKind.ClaimReward:
            {
                var set = rs != null ? RunLegalActionGenerator.TopRewards(rs) : null;
                if (set == null || a.A < 0 || a.A >= set.Rewards.Count)
                {
                    return "claim reward " + I(a.A);
                }
                var r = set.Rewards[a.A];
                return $"claim {r.RewardType} reward: {RewardDetail(r)}";
            }
            case RunActionKind.ProceedRewards:
                return "leave the rewards screen (unclaimed rewards are lost)";
            case RunActionKind.PickRewardCard:
            {
                var reward = rs != null ? RunLegalActionGenerator.OpenCardReward(rs, g.Run!.Loop) : null;
                if (reward == null || a.A < 0 || a.A >= reward.Cards.Count)
                {
                    return "take card " + I(a.A);
                }
                var card = reward.Cards[a.A].Card;
                string vars = Describe.VarSummary(card);
                return $"add {Describe.CardName(card)} ({card.Type}, {Describe.CostText(null, card)}" + (vars.Length > 0 ? ", " + vars : string.Empty) + ") to the deck";
            }
            case RunActionKind.RewardAlternative:
            {
                var alt = Alternative(g, a.A);
                return alt == null ? "card reward alternative " + I(a.A)
                    : alt.OptionId == CardRewardAlternative.SkipId ? "skip the card reward"
                    : alt.OptionId == CardRewardAlternative.RerollId ? "reroll the card reward"
                    : "card reward alternative " + alt.OptionId;
            }
            case RunActionKind.BuyEntry:
            {
                var entry = ShopEntry(g, a.A);
                if (entry == null || rs == null)
                {
                    return "buy entry " + I(a.A);
                }
                int cost = SafeCost(rs, entry);
                if (entry is MerchantCardRemovalEntry)
                {
                    return $"pay {I(cost)}g to remove a card from the deck";
                }
                var inv = ((MerchantRoom)RoomStack.CurrentRoom(rs)!).Inventory!;
                return $"buy {ShopCategory(inv, a.A, entry)} {ShopEntryId(entry)} for {I(cost)}g";
            }
            case RunActionKind.LeaveShop:
                return "leave the shop";
            case RunActionKind.ChooseRestOption:
            {
                if (rs != null && RoomStack.CurrentRoom(rs) is RestSiteRoom room && a.A >= 0 && a.A < room.Options.Count)
                {
                    string id = room.Options[a.A].OptionId;
                    return id switch
                    {
                        "HEAL" => "rest: heal 30% of max HP",
                        "SMITH" => "smith: upgrade a card",
                        _ => "rest site option " + id
                    };
                }
                return "rest site option " + I(a.A);
            }
            case RunActionKind.LeaveRestSite:
                return "leave the rest site";
            case RunActionKind.OpenChest:
                return "open the treasure chest";
            case RunActionKind.TakeTreasureRelic:
                return $"take relic {TreasureRelic(rs)}";
            case RunActionKind.SkipTreasureRelic:
                return $"skip relic {TreasureRelic(rs)}";
            case RunActionKind.ChooseEventOption:
            {
                var ev = rs != null ? RunLegalActionGenerator.CurrentEvent(rs) : null;
                if (ev != null && a.A >= 0 && a.A < ev.CurrentOptions.Count)
                {
                    var opt = ev.CurrentOptions[a.A];
                    return $"choose {opt.TextKey}" + (opt.RelicId != null ? $" (relic {opt.RelicId})" : string.Empty);
                }
                return "event option " + I(a.A);
            }
            case RunActionKind.LeaveEvent:
                return "leave the event";
            default:
                return a.ToString();
        }
    }

    private static string TreasureRelic(RunState? rs)
        => rs != null && RoomStack.CurrentRoom(rs) is TreasureRoom t ? t.CurrentRelicId ?? "?" : "?";

    public static string PotionAt(PlayerState player, int slot)
        => slot >= 0 && slot < player.PotionSlots.Length && player.PotionSlots[slot] is { } p ? p.Id : "slot " + I(slot);

    public static string RewardDetail(Reward r) => r switch
    {
        GoldReward gold => gold.Amount >= 0 ? I(gold.Amount) + " gold" : $"{I(gold.Min)}-{I(gold.Max)} gold",
        PotionReward potion => potion.PotionId ?? "potion",
        RelicReward relic => relic.RelicId ?? relic.Rarity + " relic",
        CardReward card => card.Cards.Count > 0 ? $"choose 1 of {I(card.Cards.Count)} cards" : $"choose 1 of {I(card.OptionCount)} cards",
        SpecialCardReward special => Describe.CardName(special.Card),
        CardRemovalReward => "remove a card",
        _ => r.RewardType.ToString()
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(play|potion) (\d+) (\d+)$")]
    private static partial Regex TwoNumbers();
}
