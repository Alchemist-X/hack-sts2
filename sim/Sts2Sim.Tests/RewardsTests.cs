using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Potions;
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
        var (rs, player) = NewRun(51UL);
        var encounter = EncounterRegistry.AllIds.Select(EncounterRegistry.Get).First(e => e.RoomType == RoomType.Monster && e.ShouldGiveRewards);
        var room = new CombatRoom(encounter);
        var set = RewardsCmd.GenerateForRoomEnd(rs, player, room);
        Assert.True(set.Rewards.Count > 0);
    }

    // ------------------------------------------------------------------------ clone fidelity of claimed rewards (E2c)

    /// <summary>
    /// A claimed potion reward and the belt share one PotionModel (the game's PotionReward.OnSelect hands its Potion to
    /// PotionCmd.TryToProcure, which stores that instance in Player._potionSlots). RunSim.Clone() must keep that aliasing:
    /// clone-then-DiscardPotion on the still-open Rewards screen hashes like DiscardPotion on the original.
    /// </summary>
    public static void Test_Rewards_ClaimedPotionCloneAliasesTheBelt()
    {
        var (sim, index) = RewardsScreenWithPotionReward();
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, index));
        Assert.Equal(RunPhase.Rewards, sim.Phase, "the rewards screen stays open after the claim");
        var reward = (PotionReward)RunLegalActionGenerator.TopRewards(sim.State)!.Rewards[index];
        Assert.True(reward.SuccessfullySelected, "the potion reward was claimed");
        int slot = sim.State.Players[0].GetPotionSlotIndex(reward.Potion!);
        Assert.True(slot >= 0, "the claimed reward holds the belt's PotionModel");

        var clone = sim.Clone();
        var cloneReward = (PotionReward)RunLegalActionGenerator.TopRewards(clone.State)!.Rewards[index];
        Assert.True(ReferenceEquals(cloneReward.Potion, clone.State.Players[0].PotionSlots[slot]), "the clone's reward aliases the clone's belt potion");
        Assert.False(ReferenceEquals(cloneReward.Potion, reward.Potion), "the clone shares nothing with the source");
        Assert.Equal(sim.StateHash(), clone.StateHash(), "Hash(x) == Hash(x.Clone())");
        var cloneOfClone = clone.Clone();

        var discard = RunAction.Of(RunActionKind.DiscardPotion, slot);
        Assert.True(sim.IsLegal(discard), "the rewards screen lists DiscardPotion for the claimed potion");
        sim.Step(discard);
        clone.Step(discard);
        cloneOfClone.Step(discard);
        Assert.True(reward.Potion!.HasBeenRemovedFromState, "the original's reward potion is the discarded one");
        Assert.True(cloneReward.Potion!.HasBeenRemovedFromState, "so is the clone's");
        Assert.Equal(sim.StateHash(), clone.StateHash(), "clone-then-DiscardPotion == DiscardPotion on the original");
        Assert.Equal(sim.StateHash(), cloneOfClone.StateHash(), "a clone of the clone as well");
        foreach (var next in new[] { sim.LegalActions()[0], RunAction.Of(RunActionKind.ProceedRewards) })
        {
            if (!sim.IsLegal(next))
            {
                continue;
            }
            sim.Step(next);
            clone.Step(next);
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"after {next}");
        }
    }

    /// <summary>The same aliasing through UsePotion out of combat (an AnyTime potion on a custom rewards screen).</summary>
    public static void Test_Rewards_ClaimedPotionCloneUsesTheBeltPotion()
    {
        var sim = RunSim.NewRun(new RunConfig("POTALIAS-USE", StartingDecks.IroncladCharacterId, 0) { Content = ContentFilter.PortedOnly });
        Assert.False(sim.HasPendingChoice, "the run starts at the Neow event, not at a prompt");
        var rs = sim.State;
        var player = rs.Players[0];
        player.CurrentHp = player.MaxHp / 2;
        var potion = PotionRegistry.Create(BloodPotion.PotionId);
        var reward = new PotionReward(potion, player);
        rs.RewardsStack.Add(new RewardsSet(player).WithCustomRewards(new List<Reward> { reward, new GoldReward(17, player) }));
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, 0));
        int slot = player.GetPotionSlotIndex(potion);
        Assert.True(slot >= 0 && ReferenceEquals(reward.Potion, potion), "the belt holds the reward's own potion");

        var clone = sim.Clone();
        var use = RunAction.Of(RunActionKind.UsePotion, slot, RunAction.NoIndex);
        Assert.True(sim.IsLegal(use), "an AnyTime potion is usable on the rewards screen");
        sim.Step(use);
        clone.Step(use);
        Assert.True(potion.HasBeenRemovedFromState, "the used potion is the reward's potion");
        Assert.Equal(sim.StateHash(), clone.StateHash(), "clone-then-UsePotion == UsePotion on the original");
        var gold = RunAction.Of(RunActionKind.ClaimReward, 1);
        sim.Step(gold);
        clone.Step(gold);
        Assert.Equal(sim.StateHash(), clone.StateHash(), "after claiming the gold");
    }

    /// <summary>
    /// A combat room's extra rewards are the very Reward objects its rewards set lists (the game's WithRewardsFromRoom adds
    /// <c>combatRoom.ExtraRewards[player]</c>); a clone keeps one object for both, so a claim after the clone updates the
    /// room's copy as it does in the original.
    /// </summary>
    public static void Test_Rewards_CloneSharesCombatRoomExtraRewards()
    {
        var sim = RunSim.NewRun(new RunConfig("EXTRA-ALIAS", StartingDecks.IroncladCharacterId, 0) { Content = ContentFilter.PortedOnly });
        Assert.False(sim.HasPendingChoice);
        var rs = sim.State;
        var player = rs.Players[0];
        var encounter = EncounterRegistry.AllIds.Select(EncounterRegistry.Get).First(e => e.RoomType == RoomType.Monster && e.ShouldGiveRewards);
        var room = new CombatRoom(encounter);
        room.AddExtraReward(new GoldReward(33, player));
        room.AddExtraReward(new PotionReward(PotionRegistry.Create(BloodPotion.PotionId), player));
        rs.CurrentRooms.Add(room);
        var set = new RewardsSet(player).WithRewardsFromRoom(rs, room);
        set.GenerateWithoutOffering(rs);
        rs.RewardsStack.Add(set);
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        int gold = set.Rewards.FindIndex(r => ReferenceEquals(r, room.ExtraRewards[0]));
        int potion = set.Rewards.FindIndex(r => ReferenceEquals(r, room.ExtraRewards[1]));
        Assert.True(gold >= 0 && potion >= 0, "the set lists the room's extra rewards themselves");

        var clone = sim.Clone();
        var cloneRoom = (CombatRoom)clone.State.CurrentRooms[^1];
        var cloneSet = clone.State.RewardsStack[^1];
        Assert.True(ReferenceEquals(cloneSet.Rewards[gold], cloneRoom.ExtraRewards[0]) && ReferenceEquals(cloneSet.Rewards[potion], cloneRoom.ExtraRewards[1]),
            "the clone's set lists the clone room's extra rewards");
        Assert.Equal(sim.StateHash(), clone.StateHash());
        foreach (var action in new[] { RunAction.Of(RunActionKind.ClaimReward, gold), RunAction.Of(RunActionKind.ClaimReward, potion) })
        {
            sim.Step(action);
            clone.Step(action);
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"after {action}");
        }
        Assert.True(room.ExtraRewards.All(r => r.SuccessfullySelected) && cloneRoom.ExtraRewards.All(r => r.SuccessfullySelected), "the claims reach the rooms");
        var discard = RunAction.Of(RunActionKind.DiscardPotion, player.GetPotionSlotIndex(((PotionReward)room.ExtraRewards[1]).Potion!));
        sim.Step(discard);
        clone.Step(discard);
        Assert.Equal(sim.StateHash(), clone.StateHash(), "an extra potion reward also aliases the belt");
    }

    /// <summary>
    /// A picked card-reward card and a taken special-card reward's card are the instances put into the deck. A clone taken
    /// after the claims must follow later edits of those deck cards like the original does (here WHETSTONE, claimed after
    /// the clone, upgrades both: they are the deck's only upgradable attacks).
    /// </summary>
    public static void Test_Rewards_ClonedRewardCardsFollowTheDeck()
    {
        var sim = RunSim.NewRun(new RunConfig("CHOSEN-ALIAS", StartingDecks.IroncladCharacterId, 0) { Content = ContentFilter.PortedOnly });
        Assert.False(sim.HasPendingChoice);
        var rs = sim.State;
        var player = rs.Players[0];
        player.Deck.RemoveAll(c => c.Type == CardType.Attack);
        var offered = CardFactory.CreateRunCard(rs, CardRegistry.Get(StrikeIronclad.CardId), player);
        var cardReward = new CardReward(new[] { offered }, CardCreationSource.Encounter, player, CardCreationOptions.ForRoom(rs, player, RoomType.Monster));
        var special = new SpecialCardReward(CardFactory.CreateRunCard(rs, CardRegistry.Get(Bash.CardId), player), player);
        var relicReward = new RelicReward(RelicRegistry.Create(Whetstone.RelicId), player);
        rs.RewardsStack.Add(new RewardsSet(player).WithCustomRewards(new List<Reward> { relicReward, cardReward, special }));
        RunLoop.AfterAction(rs, sim.Loop);
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, 1));
        Assert.Equal(RunPhase.CardReward, sim.Phase);
        sim.Step(RunAction.Of(RunActionKind.PickRewardCard, 0));
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, 2));
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        var chosen = cardReward.ChosenCards.Single();
        Assert.True(ReferenceEquals(chosen, player.Deck[^2]) && ReferenceEquals(special.Card, player.Deck[^1]), "the rewards hold the deck's cards");

        var clone = sim.Clone();
        var cloneDeck = clone.State.Players[0].Deck;
        var cloneRewards = clone.State.RewardsStack[^1].Rewards;
        var cloneChosen = ((CardReward)cloneRewards[1]).ChosenCards[0];
        var cloneSpecial = ((SpecialCardReward)cloneRewards[2]).Card;
        Assert.True(ReferenceEquals(cloneChosen, cloneDeck[^2]) && ReferenceEquals(cloneSpecial, cloneDeck[^1]), "the clone's rewards hold the clone's deck cards");
        Assert.Equal(sim.StateHash(), clone.StateHash());
        var claimRelic = RunAction.Of(RunActionKind.ClaimReward, 0);
        sim.Step(claimRelic);
        clone.Step(claimRelic);
        Assert.True(chosen.UpgradeLevel == 1 && special.Card.UpgradeLevel == 1, "WHETSTONE upgraded both reward cards");
        Assert.True(cloneChosen.UpgradeLevel == 1 && cloneSpecial.UpgradeLevel == 1, "in the clone as well");
        Assert.Equal(sim.StateHash(), clone.StateHash(), "clone-then-claim == claim on the original");
    }

    /// <summary>
    /// Seeded Ironclad runs (boosted HP, uniform random actions without potion discards) up to the first Rewards screen
    /// that offers an unclaimed potion reward while the belt has a free slot.
    /// </summary>
    private static (RunSim Sim, int RewardIndex) RewardsScreenWithPotionReward()
    {
        var legal = new List<RunAction>(32);
        for (int seed = 1; seed <= 40; seed++)
        {
            var sim = RunSim.NewRun(new RunConfig($"POTALIAS-{seed}", StartingDecks.IroncladCharacterId, 0) { Content = ContentFilter.PortedOnly });
            var random = new System.Random(seed);
            try
            {
                for (int step = 0; step < 3_000 && !sim.IsTerminal; step++)
                {
                    var player = sim.State.Players[0];
                    if (!sim.HasPendingChoice && player.CurrentHp > 0 && player.CurrentHp < 300)
                    {
                        player.MaxHp = Math.Max(player.MaxHp, 300);
                        player.CurrentHp = player.MaxHp;
                    }
                    if (sim.Phase == RunPhase.Rewards && !sim.HasPendingChoice && player.HasOpenPotionSlot
                        && RunLegalActionGenerator.TopRewards(sim.State) is { } set)
                    {
                        int index = set.Rewards.FindIndex(r => r is PotionReward && !r.SuccessfullySelected);
                        if (index >= 0)
                        {
                            return (sim, index);
                        }
                    }
                    sim.LegalActions(legal);
                    if (legal.Count > 1)
                    {
                        legal.RemoveAll(a => a.Kind == RunActionKind.DiscardPotion);
                    }
                    sim.Step(legal[random.Next(legal.Count)]);
                }
            }
            catch (NotImplementedException)
            {
                // an unported event or card on this seed: try the next one
            }
        }
        Assert.Fail("no Rewards screen with a potion reward in 40 seeded runs");
        return default;
    }
}
