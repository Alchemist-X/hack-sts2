using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace Sts2Recorder.Game;

/// <summary>
/// R4 reward-offer introspection shared by the rewards state (states.jsonl)
/// and the reward_taken / card_reward_rerolled records. Offers are populated
/// before the rewards screen opens (RewardsSet.cs:206 GenerateWithoutOffering,
/// :307 ShowScreen). Ids here use the full ModelId form of the actions and
/// events streams; the state builder renders its own Entry-form view.
/// </summary>
internal static class RewardOffers
{
    private static AccessTools.FieldRef<SpecialCardReward, CardModel?>? _specialCardRef;
    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        RecorderMod.TrySubscribe(
            "fieldref:SpecialCardReward._card",
            () => _specialCardRef = AccessTools.FieldRefAccess<SpecialCardReward, CardModel?>("_card"));
    }

    /// <summary>The card a SpecialCardReward offers (private field; null when unreadable).</summary>
    internal static CardModel? SpecialCard(SpecialCardReward reward)
    {
        var fieldRef = _specialCardRef;
        if (fieldRef == null)
        {
            return null;
        }
        try
        {
            return fieldRef(reward);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>{id, upgrade_level, enchantment {id, amount} | null} with full ModelId strings.</summary>
    internal static JsonNode? CardIdentity(CardModel? card)
    {
        if (card == null)
        {
            return null;
        }
        var node = new JsonObject
        {
            ["id"] = JsonDescribe.Try(() => card.Id.ToString()),
            ["upgrade_level"] = JsonDescribe.Try<int?>(() => card.CurrentUpgradeLevel),
        };
        node["enchantment"] = JsonDescribe.Try<JsonNode>(() =>
        {
            var enchantment = card.Enchantment;
            return enchantment == null
                ? null
                : new JsonObject
                {
                    ["id"] = enchantment.Id.ToString(),
                    ["amount"] = enchantment.Amount,
                };
        });
        return node;
    }

    internal static JsonArray CardIdentities(IEnumerable<CardModel> cards)
    {
        var array = new JsonArray();
        foreach (var card in cards)
        {
            array.Add(CardIdentity(card));
        }
        return array;
    }

    /// <summary>What a reward offers right now (before it is claimed).</summary>
    internal static JsonObject Describe(Reward reward)
    {
        var offer = new JsonObject
        {
            ["reward_type"] = JsonDescribe.SnakeCase(reward.GetType().Name),
        };
        try
        {
            switch (reward)
            {
                case CardReward cardReward:
                    offer["cards"] = CardIdentities(cardReward.Cards);
                    offer["can_reroll"] = JsonDescribe.Try<bool?>(() => cardReward.CanReroll);
                    offer["can_skip"] = JsonDescribe.Try<bool?>(() => cardReward.CanSkip);
                    break;
                case SpecialCardReward specialCardReward:
                    offer["card"] = CardIdentity(SpecialCard(specialCardReward));
                    break;
                case RelicReward relicReward:
                    offer["relic_id"] = JsonDescribe.Try(() => relicReward.Relic?.Id.ToString());
                    break;
                case PotionReward potionReward:
                    offer["potion_id"] = JsonDescribe.Try(() => potionReward.Potion?.Id.ToString());
                    break;
                case GoldReward goldReward:
                    offer["amount"] = JsonDescribe.Try<int?>(() => goldReward.Amount);
                    break;
                case LinkedRewardSet linkedRewardSet:
                {
                    var linked = new JsonArray();
                    foreach (var inner in linkedRewardSet.Rewards)
                    {
                        linked.Add(Describe(inner));
                    }
                    offer["rewards"] = linked;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            offer["describe_error"] = ex.GetType().Name + ": " + ex.Message;
        }
        return offer;
    }
}
