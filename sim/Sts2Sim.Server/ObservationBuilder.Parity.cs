using System.Globalization;
using System.Text;
using Sts2Sim.Diff;

namespace Sts2Sim.Server;

// UI-parity additions (docs/parity-audit-2026-11-02.md): what the game shows a human that the observation lacked
// before — the act's bosses (map + top bar), card piles (pile screens), orb numbers, rest-site option descriptions.
public static partial class ObservationBuilder
{
    // The current act's boss encounters as the map's boss points and the top-bar boss icon show them
    // (Nodes.Screens.Map/NBossMapPoint.cs:122 BossEncounter / SecondBossEncounter; TopBar/NTopBarBossIcon.cs).
    private static JsonArray? BossesNode(RunState rs)
    {
        if (rs.Acts.Count == 0)
        {
            return null;
        }
        var rooms = rs.Act.Rooms;
        var list = new JsonArray();
        foreach (var enc in new[] { rooms.Boss, rooms.SecondBoss })
        {
            if (enc == null)
            {
                continue;
            }
            string id = enc.Id;
            string monster = id.EndsWith(RunLoop.BossEncounterSuffix, StringComparison.Ordinal) ? id[..^RunLoop.BossEncounterSuffix.Length] : id;
            list.Add(new JsonObject { ["id"] = id, ["name"] = Rt(() => RulesText.EnemyTitle(monster)) });
        }
        return list;
    }

    // The act's Ancient, whose portrait marks its map point (Nodes.Screens.Map/NAncientMapPoint.cs:124
    // `_runState.Act.Ancient.MapIcon`): {"id", "name"} or null.
    private static JsonObject? AncientNode(RunState rs)
    {
        if (rs.Acts.Count == 0 || rs.Act.Rooms.AncientId is not { } id)
        {
            return null;
        }
        return new JsonObject { ["id"] = id, ["name"] = Rt(() => RulesText.EventTitle(id)) };
    }

    // "boss: CEREMONIAL_BEAST_BOSS (Ceremonial Beast)[, then <second boss>][ | ancient: NEOW (Neow)]" or null.
    internal static string? BossLine(JsonArray? bosses, JsonObject? ancient)
    {
        if (bosses == null || bosses.Count == 0)
        {
            return null;
        }
        var sb = new StringBuilder("boss: ");
        for (int i = 0; i < bosses.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", then ");
            }
            sb.Append(bosses[i]!["id"]!.GetValue<string>());
            AppendName(sb, bosses[i]);
        }
        if (ancient != null)
        {
            sb.Append(" | ancient: ").Append(ancient["id"]!.GetValue<string>());
            AppendName(sb, ancient);
        }
        return sb.ToString();
    }

    // "orbs (3 slots; passive/evoke): LIGHTNING 3/8, FROST 2/5" (the numbers the orbs show in combat), ids only
    // out of combat.
    private static string OrbsLine(JsonArray? orbs, int slots)
    {
        var sb = new StringBuilder("orbs (").Append(I(slots)).Append(" slots");
        bool values = orbs != null && orbs.Count > 0 && orbs.All(o => o?["passive"] != null && o["evoke"] != null);
        sb.Append(values ? "; passive/evoke): " : "): ");
        if (orbs == null || orbs.Count == 0)
        {
            return sb.Append('-').ToString();
        }
        sb.Append(string.Join(", ", orbs.Select(o => values
            ? o!["id"]!.GetValue<string>() + " " + o["passive"]!.ToJsonString() + "/" + o["evoke"]!.ToJsonString()
            : o!["id"]!.GetValue<string>())));
        return sb.ToString();
    }

    // A card as the deck and pile screens show it: id (+upgrades) and its enchantment / affliction marks.
    public static string CardLabel(CardInstance card)
    {
        var sb = new StringBuilder(Describe.CardName(card));
        if (card.Affliction is { } aff)
        {
            sb.Append('[').Append(aff.Id);
            if (aff.Amount > 1)
            {
                sb.Append(' ').Append(I(aff.Amount));
            }
            sb.Append(']');
        }
        if (card.Enchantment is { } ench)
        {
            sb.Append("[ench ").Append(ench.Id).Append(' ').Append(I(ench.Amount)).Append(']');
        }
        return sb.ToString();
    }

    // Comma list of card labels with "xN" counts (see CardGroups).
    internal static string CardsSummary(IEnumerable<CardInstance> cards, bool sorted, bool groupAll = true)
    {
        var groups = CardGroups(cards, sorted, groupAll);
        return groups.Count == 0 ? "-" : string.Join(", ", groups.Select(g => g.Count > 1 ? g.Label + " x" + I(g.Count) : g.Label));
    }

    // Card labels grouped with counts. sorted = the draw pile's view (the game sorts it, NCardPileScreen.cs:185, so
    // the draw order stays hidden): one group per label in ordinal order. Otherwise the pile's own order (the discard
    // / exhaust screens keep it, NCardPileScreen SortingOrders.Ascending): runs of equal labels; groupAll (the deck)
    // groups equal labels at their first appearance.
    internal static List<(string Label, int Count)> CardGroups(IEnumerable<CardInstance> cards, bool sorted, bool groupAll)
    {
        var labels = cards.Select(CardLabel).ToList();
        var groups = new List<(string Label, int Count)>();
        if (sorted)
        {
            labels.Sort(StringComparer.Ordinal);
        }
        if (sorted || groupAll)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var l in labels)
            {
                if (index.TryGetValue(l, out int k))
                {
                    groups[k] = (l, groups[k].Count + 1);
                }
                else
                {
                    index[l] = groups.Count;
                    groups.Add((l, 1));
                }
            }
        }
        else
        {
            foreach (var l in labels)
            {
                if (groups.Count > 0 && groups[^1].Label == l)
                {
                    groups[^1] = (l, groups[^1].Count + 1);
                }
                else
                {
                    groups.Add((l, 1));
                }
            }
        }
        return groups;
    }

    // combat.pile_view: {"draw": [{"card": label, "n": count}] sorted, "discard": [...] and "exhaust": [...] in pile
    // order (oldest first, runs of equal cards merged)}. What the pile screens show.
    private static JsonObject PileViewNode(PlayerState player)
    {
        JsonArray Groups(IEnumerable<CardInstance> cards, bool sorted)
        {
            var arr = new JsonArray();
            foreach (var (label, count) in CardGroups(cards, sorted, groupAll: false))
            {
                arr.Add(new JsonObject { ["card"] = label, ["n"] = count });
            }
            return arr;
        }
        return new JsonObject
        {
            ["draw"] = Groups(player.DrawPile.Cards, true),
            ["discard"] = Groups(player.DiscardPile.Cards, false),
            ["exhaust"] = Groups(player.ExhaustPile.Cards, false)
        };
    }

    // The three combat piles a player can open at any time (NDrawPileButton / NDiscardPileButton / NExhaustPileButton
    // -> NCardPileScreen): the draw pile sorted, discard and exhaust in pile order (oldest first). Empty piles are
    // left out (the hand header carries the counts).
    private static void AppendPiles(StringBuilder sb, JsonObject? view)
    {
        if (view == null)
        {
            return;
        }
        foreach (var (key, title) in new[] { ("draw", "draw pile (sorted): "), ("discard", "discard pile (oldest first): "), ("exhaust", "exhaust pile (oldest first): ") })
        {
            if (view[key] is JsonArray { Count: > 0 } groups)
            {
                sb.Append(title).Append(string.Join(", ", groups.Select(g =>
                {
                    int n = g!["n"]!.GetValue<int>();
                    return g["card"]!.GetValue<string>() + (n > 1 ? " x" + I(n) : string.Empty);
                }))).Append('\n');
            }
        }
    }

    // The rest-site button's description (Entities.RestSite/<Option>RestSiteOption.cs Description; the rest_site_ui
    // localisation table is not exported, so the wording is the sim's, the numbers the game's).
    private static string? RestOptionText(RunState rs, RestSiteOption option)
    {
        var owner = option.Owner;
        switch (option)
        {
            case HealRestSiteOption:
                return $"Heal {I((int)HealRestSiteOption.GetHealAmount(rs, owner))} HP (30% of your Max HP, after relics).";
            case SmithRestSiteOption smith:
                return smith.SmithCount == 1 ? "Upgrade a card in your Deck." : $"Upgrade {I(smith.SmithCount)} cards in your Deck.";
            case DigRestSiteOption:
                return "Obtain a random Relic.";
            case CookRestSiteOption:
                return $"Remove {I(CookRestSiteOption.CardsToRemove)} cards from your Deck. Gain {I(CookRestSiteOption.MaxHpGain)} Max HP.";
            case HatchRestSiteOption:
                return $"Obtain {RulesText.RelicTitle(HatchRestSiteOption.ByrdpipId) ?? HatchRestSiteOption.ByrdpipId}.";
            case LiftRestSiteOption:
                return owner.GetRelic<Girya>() is { } girya ? $"Lift the Girya: gain Strength in combat ({I(3 - girya.TimesLifted)} lifts left)." : "Lift the Girya.";
            case KindleRestSiteOption:
                return "Rekindle your Pumpkin Candle.";
            case CloneRestSiteOption:
                return $"Duplicate every card enchanted with Clone ({I(owner.Deck.Count(c => c.Enchantment is Sts2Sim.Core.Cards.Enchantments.Clone))}).";
            default:
                return null;
        }
    }
}
