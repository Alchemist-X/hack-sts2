using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class RewardsTests
{
    private static (RunState Rs, PlayerState Player) NewRun(ulong seed, int ascension = 0)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), ascension);
        player.PlayerRng = new PlayerRngSet(seed + 1);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, ascension);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(rs, player, rs.Rng.UpFront);
        rs.SharedRelicGrabBag = new RelicGrabBag(true);
        rs.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(rs), rs.Rng.UpFront);
        rs.Odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
        return (rs, player);
    }

    private static byte[] Canonical(ICanonical value)
    {
        var w = new CanonicalWriter();
        value.WriteCanonical(w);
        return w.ToArray();
    }

    public static void Test_Rewards_CardRarityOffsetAccumulatesAsFloat()
    {
        var rng = new Rng(42UL);
        var mirror = rng.DeepClone();
        var odds = new CardRarityOdds(rng, 0);
        float offset = -0.05f;
        for (int i = 0; i < 200; i++)
        {
            var rarity = odds.Roll(CardRarityOddsType.RegularEncounter);
            float r = mirror.NextFloat();
            float rare = 0.03f + offset;
            var expected = r < rare ? CardRarity.Rare : (r < 0.37f + rare ? CardRarity.Uncommon : CardRarity.Common);
            Assert.Equal(expected, rarity, $"roll {i}");
            offset = expected == CardRarity.Rare ? -0.05f : Math.Min(offset + 0.01f, 0.4f);
            Assert.Equal(BitConverter.SingleToUInt32Bits(offset), BitConverter.SingleToUInt32Bits(odds.CurrentValue), $"offset bits after roll {i}");
        }
    }

    public static void Test_Rewards_CardRarityScarcityAndBossOdds()
    {
        var odds = new CardRarityOdds(new Rng(7UL), (int)AscensionLevel.Scarcity);
        Assert.Equal(0.005f, odds.RarityGrowth);
        Assert.Equal(0.0149f, odds.GetBaseOdds(CardRarityOddsType.RegularEncounter, CardRarity.Rare));
        Assert.Equal(0.05f, odds.GetBaseOdds(CardRarityOddsType.EliteEncounter, CardRarity.Rare));
        odds.OverrideCurrentValue(-0.05f);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(CardRarity.Rare, odds.Roll(CardRarityOddsType.BossEncounter), "boss rewards are always rare");
            Assert.Equal(-0.05f, odds.CurrentValue, "a rare resets the offset");
        }
        float before = odds.CurrentValue;
        odds.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop);
        odds.RollWithBaseOdds(CardRarityOddsType.Uniform);
        Assert.Equal(before, odds.CurrentValue, "shop and base-odds rolls leave the offset alone");
    }

    public static void Test_Rewards_PotionRewardOddsStepAndEliteBonus()
    {
        var (rs, player) = NewRun(3UL);
        var mirror = player.PlayerRng!.Rewards.DeepClone();
        var odds = player.PlayerOdds!.PotionReward;
        float current = 0.4f;
        for (int i = 0; i < 50; i++)
        {
            var roomType = i % 3 == 0 ? RoomType.Elite : RoomType.Monster;
            bool hit = odds.Roll(rs, player, roomType);
            float threshold = current + (roomType == RoomType.Elite ? 0.25f : 0f) * 0.5f;
            bool expected = mirror.NextFloat() < threshold;
            Assert.Equal(expected, hit, $"roll {i}");
            current = expected ? current - 0.1f : current + 0.1f;
            Assert.Equal(current, odds.CurrentValue, $"value after roll {i}");
        }
    }

    public static void Test_Rewards_UnknownMapPointOddsRollAndReset()
    {
        var (rs, _) = NewRun(11UL);
        var odds = rs.Odds!.UnknownMapPoint;
        var mirror = new Rng(rs.Rng.UnknownMapPoint.ToSnapshot());
        float monster = 0.1f, elite = -1f, treasure = 0.02f, shop = 0.03f;
        for (int i = 0; i < 40; i++)
        {
            var rolled = odds.Roll(Array.Empty<RoomType>(), rs);
            float r = mirror.NextFloat();
            var expected = RoomType.Event;
            float acc = 0f;
            foreach (var (type, value) in new[] { (RoomType.Monster, monster), (RoomType.Elite, elite), (RoomType.Treasure, treasure), (RoomType.Shop, shop) })
            {
                if (value < 0f)
                {
                    continue;
                }
                acc += value;
                if (r <= acc)
                {
                    expected = type;
                    break;
                }
            }
            Assert.Equal(expected, rolled, $"roll {i}");
            monster = expected == RoomType.Monster ? 0.1f : monster + 0.1f;
            elite = expected == RoomType.Elite ? -1f : elite + -1f;
            treasure = expected == RoomType.Treasure ? 0.02f : treasure + 0.02f;
            shop = expected == RoomType.Shop ? 0.03f : shop + 0.03f;
            Assert.Equal(monster, odds.MonsterOdds, $"monster odds after roll {i}");
            Assert.Equal(shop, odds.ShopOdds, $"shop odds after roll {i}");
        }
        odds.ResetToBase();
        Assert.Equal(0.1f, odds.MonsterOdds);
        Assert.Equal(-1f, odds.EliteOdds);
        Assert.Equal(0.02f, odds.TreasureOdds);
        Assert.Equal(0.03f, odds.ShopOdds);
        Assert.Equal(1f - (0.1f + 0.02f + 0.03f), odds.EventOdds);
    }

    public static void Test_Rewards_RelicCatalogCoversEveryPool()
    {
        foreach (var pool in RelicPools.AllRelicPools)
        {
            foreach (var id in pool.AllRelicIds)
            {
                Assert.True(RelicCatalog.Contains(id), $"{pool.Id}/{id} has a rarity");
            }
        }
        Assert.Equal(RelicRarity.Common, RelicCatalog.RarityOf("ANCHOR"));
        var locked = RelicCatalog.GetUnlockedRelics(RelicPools.SharedRelicPool, UnlockProfile.None);
        var all = RelicCatalog.GetUnlockedRelics(RelicPools.SharedRelicPool, UnlockProfile.All);
        Assert.Equal(all.Count - 15, locked.Count, "Relic1-5 epochs gate three shared relics each");
    }

    public static void Test_Rewards_PlayerRelicBagPopulateAndPull()
    {
        var (rs, player) = NewRun(5UL);
        var bag = player.RelicGrabBag!;
        Assert.SequenceEqual(new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop }.OrderBy(r => r), bag.DequeOrder.OrderBy(r => r));
        foreach (var rarity in bag.DequeOrder)
        {
            foreach (var id in bag.GetDequeContents(rarity))
            {
                Assert.Equal(rarity, RelicCatalog.RarityOf(id), $"{id} sits in its rarity deque");
            }
        }
        var commons = bag.GetDequeContents(RelicRarity.Common).ToList();
        string? front = bag.PullFromFront(RelicRarity.Common, rs);
        Assert.Equal(commons[0], front, "front pull takes the first common");
        var shops = bag.GetDequeContents(RelicRarity.Shop).ToList();
        string? back = bag.PullFromBack(RelicRarity.Shop, _ => true, rs);
        Assert.Equal(shops[^1], back, "back pull takes the last shop relic");
        Assert.False(bag.Contains(front!));
        var clone = bag.DeepClone();
        Assert.SequenceEqual(Canonical(bag), Canonical(clone), "clone canonical");
    }

    public static void Test_Rewards_RelicBagRefreshAndFallback()
    {
        var (rs, _) = NewRun(9UL);
        var refreshing = new RelicGrabBag(true);
        refreshing.Populate(new[] { "ANCHOR", "AKABEKO" }, new Rng(1UL));
        Assert.Equal("ANCHOR", refreshing.PullFromFront(RelicRarity.Common, rs));
        Assert.Equal("ANCHOR", refreshing.PullFromFront(RelicRarity.Common, rs), "an emptied deque refills from the original list");
        var fixedBag = new RelicGrabBag(false);
        fixedBag.Populate(new[] { "ANCHOR", "AKABEKO" }, new Rng(1UL));
        Assert.Equal("ANCHOR", fixedBag.PullFromFront(RelicRarity.Common, rs));
        Assert.Equal("AKABEKO", fixedBag.PullFromFront(RelicRarity.Common, rs), "no refresh: common falls back to uncommon");
        Assert.Null(fixedBag.PullFromFront(RelicRarity.Common, rs), "nothing left");
        var loaded = RelicGrabBag.FromSerializable(new[] { new KeyValuePair<RelicRarity, IReadOnlyList<string>>(RelicRarity.Common, new[] { "ANCHOR", "NOT_A_RELIC" }) });
        Assert.False(loaded.RefreshAllowed, "a loaded bag never refreshes");
        Assert.SequenceEqual(new[] { "ANCHOR" }, loaded.GetDequeContents(RelicRarity.Common), "unknown ids are dropped on load");
    }

    public static void Test_Rewards_RelicRarityRollUsesPlayerRewards()
    {
        var (_, player) = NewRun(13UL);
        var mirror = player.PlayerRng!.Rewards.DeepClone();
        for (int i = 0; i < 30; i++)
        {
            float r = mirror.NextFloat();
            var expected = r < 0.5f ? RelicRarity.Common : (r < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare);
            Assert.Equal(expected, RelicFactory.RollRarity(player), $"roll {i}");
        }
    }

    public static void Test_Rewards_CardRewardDrawOrderOnRewardsStream()
    {
        var (rs, player) = NewRun(21UL);
        rs.CurrentActIndex = 1;
        var mirror = player.PlayerRng!.Rewards.DeepClone();
        var mirrorOdds = new CardRarityOdds(mirror, 0);
        var options = CardCreationOptions.ForRoom(rs, player, RoomType.Monster).WithFlags(CardCreationFlags.IsFromCombat);
        var pool = options.GetPossibleCards(rs, player).ToList();
        var results = CardFactory.CreateForReward(rs, player, 3, options).ToList();
        Assert.Equal(3, results.Count);
        var offered = new List<CardDef>();
        for (int i = 0; i < 3; i++)
        {
            var candidates = pool.Except(offered).ToList();
            var rarity = mirrorOdds.Roll(CardRarityOddsType.RegularEncounter);
            while (!candidates.Any(c => c.Rarity == rarity))
            {
                rarity = CardFactory.GetNextHighestRarityWithWrapping(rarity);
            }
            var pick = mirror.NextItem(candidates.Where(c => c.Rarity == rarity))!;
            Assert.Equal(pick.Id, results[i].Card.Id, $"card {i}");
            offered.Add(pick);
            decimal roll = (decimal)mirror.NextFloat();
            bool upgraded = pick.MaxUpgradeLevel > 0 && pick.Rarity != CardRarity.Rare && roll <= 0.25m;
            Assert.Equal(upgraded, results[i].Card.UpgradeLevel > 0, $"upgrade roll {i}");
        }
        Assert.Equal(mirror.Counter, player.PlayerRng.Rewards.Counter, "rng counters agree");
        Assert.Equal(mirrorOdds.CurrentValue, player.PlayerOdds!.CardRarity.CurrentValue, "card rarity offset");
    }

    public static void Test_Rewards_CustomSetSortsAndClaims()
    {
        var (rs, player) = NewRun(31UL);
        var options = CardCreationOptions.ForRoom(rs, player, RoomType.Elite);
        var rewards = new List<Reward>
        {
            new CardReward(player, options, 3),
            new RelicReward(player),
            new GoldReward(10, 20, player),
        };
        var set = RewardsCmd.GenerateCustom(rs, player, rewards);
        Assert.SequenceEqual(new[] { RewardType.Gold, RewardType.Relic, RewardType.Card }, set.Rewards.Select(r => r.RewardType), "sorted by RewardsSetIndex");
        Assert.True(set.Rewards.All(r => r.IsPopulated));
        var gold = (GoldReward)set.Rewards[0];
        Assert.True(gold.Amount >= 10 && gold.Amount <= 20);
        int goldBefore = player.Gold;
        Assert.True(gold.OnSelectWrapper(rs));
        Assert.Equal(goldBefore + gold.Amount, player.Gold);
        Assert.True(gold.SuccessfullySelected);

        var card = (CardReward)set.Rewards[2];
        Assert.False(card.OnSelectWrapper(rs), "opening the card screen does not complete the reward");
        Assert.True(card.IsSelecting);
        Assert.SequenceEqual(new[] { CardRewardAlternative.SkipId }, card.Alternatives.Select(a => a.OptionId));
        Assert.True(card.ChooseAlternative(rs, 0), "skip ends the selection");
        Assert.False(card.SuccessfullySelected, "a skipped card reward stays claimable");
        card.OnSelectWrapper(rs);
        int deckBefore = player.Deck.Count;
        string pickedId = card.Cards[1].Card.Id;
        Assert.True(card.PickCard(rs, 1));
        Assert.True(card.SuccessfullySelected);
        Assert.Equal(deckBefore + 1, player.Deck.Count);
        Assert.Equal(pickedId, player.Deck[^1].Id);
    }

    public static void Test_Rewards_SetCloneIsCanonicalEqual()
    {
        var (rs, player) = NewRun(41UL);
        var rewards = new List<Reward> { new GoldReward(25, player), new CardReward(player, CardCreationOptions.ForRoom(rs, player, RoomType.Monster), 3) };
        var set = RewardsCmd.GenerateCustom(rs, player, rewards);
        var otherPlayer = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3) { PlayerRng = player.PlayerRng!.DeepClone() };
        var clone = set.CloneFor(otherPlayer, null);
        Assert.SequenceEqual(Canonical(set), Canonical(clone), "rewards set clone canonical");
        var oddsClone = player.PlayerOdds!.CloneFor(otherPlayer.PlayerRng!);
        var w1 = new CanonicalWriter();
        player.PlayerOdds.WriteCanonical(w1);
        var w2 = new CanonicalWriter();
        oddsClone.WriteCanonical(w2);
        Assert.SequenceEqual(w1.ToArray(), w2.ToArray(), "player odds clone canonical");
    }

    public static void Test_Rewards_RoomEndRewardsNeedRooms()
    {
        W1Pending.Run("Rewards.RoomEndRewards", "rooms", () =>
        {
            var (rs, player) = NewRun(51UL);
            var room = new CombatRoom(EncounterRegistry.Get(EncounterRegistry.AllIds[0]));
            var set = RewardsCmd.GenerateForRoomEnd(rs, player, room);
            Assert.True(set.Rewards.Count > 0);
        });
    }
}
