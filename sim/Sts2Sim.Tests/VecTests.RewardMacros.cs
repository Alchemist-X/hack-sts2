using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Rewards-screen macro candidates (RewardMacros, deck-building channel 2026-11-04): a card reward is listed as one
/// TakeRewardCard row per offered card and one TakeRewardAlternative row per alternative other than Skip, each expanding to
/// the game's ClaimReward + PickRewardCard / RewardAlternative; a potion reward the game would refuse (full belt) is not
/// listed; the offered cards are shown in <c>reward_choices</c>.
/// </summary>
public static unsafe partial class VecTests
{
    /// <summary>A fresh Ironclad run (opening prompt cleared) put on a rewards screen holding <paramref name="rewards"/>.</summary>
    private static RunSim RewardsStart(string seed, Func<RunState, PlayerState, List<Reward>> rewards)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var rs = sim.State;
        var p = rs.Players[0];
        rs.RewardsStack.Add(new RewardsSet(p).WithCustomRewards(rewards(rs, p)));
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        return sim;
    }

    private static CardReward OfferedCards(RunState rs, PlayerState p, params string[] ids)
        => new(ids.Select(id => CardFactory.CreateRunCard(rs, CardRegistry.Get(id), p)), CardCreationSource.Encounter, p,
            CardCreationOptions.ForRoom(rs, p, RoomType.Monster));

    /// <summary>Restores a clone of <paramref name="sim"/> into slot 0 of a fresh one-slot env.</summary>
    private static VecEnv Observe(RunSim sim, NativeBuffers nb)
    {
        var env = NewEnv(1, 1, 0);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        return env;
    }

    private static List<RunAction> CandList(NativeBuffers nb, int slot = 0)
    {
        var list = new List<RunAction>();
        for (int r = 0; r < nb.Slot(slot).CandN; r++)
        {
            list.Add(CandidateTable.ToAction(CandRow(nb, slot, r)));
        }
        return list;
    }

    public static void Test_VecCand_RewardMacroTakesAnOfferedCard()
    {
        var sim = RewardsStart("MACRO-TAKE", (rs, p) =>
        {
            var cards = OfferedCards(rs, p, PommelStrike.CardId, ShrugItOff.CardId, Inflame.CardId);
            cards.Cards[1].Card.Upgrade();
            return new List<Reward> { new GoldReward(25, p), cards };
        });
        var rs0 = sim.State;
        var reward = (CardReward)rs0.RewardsStack[^1].Rewards[1];
        Assert.Equal(1, reward.Cards[1].Card.UpgradeLevel);
        var take = RewardMacros.TakeRewardCard;
        var expect = new List<RunAction>
        {
            RunAction.Of(RunActionKind.ClaimReward, 0),
            RunAction.Of(take, 1, 0), RunAction.Of(take, 1, 1), RunAction.Of(take, 1, 2),
            RunAction.Of(RunActionKind.ProceedRewards),
        };
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.ClaimReward, 0), RunAction.Of(RunActionKind.ClaimReward, 1), RunAction.Of(RunActionKind.ProceedRewards) },
            sim.LegalActions(), "RunSim's own legal actions are unchanged");
        Assert.SequenceEqual(expect, RewardMacros.Candidates(sim));

        using var nb = new NativeBuffers(1);
        var env = Observe(sim, nb);
        var slot = nb.Slot(0);
        Assert.SequenceEqual(expect, CandList(nb));
        int w = Schema.RewardChoiceFields.Length;
        int hc = Schema.HC;
        for (int i = 0; i < 3; i++)
        {
            var row = CandRow(nb, 0, 1 + i);
            Assert.Equal(i, row[CandCol.OptionIndex], "the k-th macro row points at reward_choices row k");
            Assert.Equal(Vocab.Card(reward.Cards[i].Card.Id), row[CandCol.CardId]);
            Assert.Equal(-1, row[CandCol.HandSlot]);
            var rc = slot.RewardChoices.Slice(i * w, w);
            Assert.Equal(1, rc[RcCol.RewardIndex]);
            Assert.Equal(i, rc[RcCol.Option]);
            Assert.Equal(0, rc[RcCol.AltId]);
        }
        Assert.True(slot.RewardChoices[(3 * w)..].ToArray().All(v => v == 0), "rows past the choices are zero");
        Assert.True(slot.CardReward.ToArray().All(v => v == 0) && slot.CardRewardAlts.ToArray().All(v => v == 0),
            "card_reward / card_reward_alts keep their meaning: the open card screen only");
        Assert.Equal(1, slot.Rewards[1 * 4 + 3], "rewards row 1 is the card reward");

        // reward_choices shows each card exactly as the opened card screen's card_reward row does
        var opened = sim.Clone();
        opened.Step(RunAction.Of(RunActionKind.ClaimReward, 1));
        Assert.Equal(RunPhase.CardReward, opened.Phase);
        using (var ob = new NativeBuffers(1))
        {
            Observe(opened, ob);
            for (int i = 0; i < 3; i++)
            {
                Assert.SequenceEqual(ob.Slot(0).CardReward.Slice(i * hc, hc).ToArray(), slot.RewardChoices.Slice(i * w + RcCol.Card, hc).ToArray(), $"card {i}");
            }
            Assert.Equal(1, slot.RewardChoices[1 * w + RcCol.Card + HC.Upgrade], "the upgraded offer shows upgrade 1");
            Assert.True(ob.Slot(0).RewardChoices.ToArray().All(v => v == 0), "no reward_choices while the card screen is open");
        }

        // one decision = the game's two clicks: same state as ClaimReward(1) + PickRewardCard(1)
        var reference = sim.Clone();
        reference.Step(RunAction.Of(RunActionKind.ClaimReward, 1));
        reference.Step(RunAction.Of(RunActionKind.PickRewardCard, 1));
        int deck = sim.State.Players[0].Deck.Count;
        env.Step(new[] { 2 }, nb.Table);
        Assert.Equal(0, slot.Status & (int)(SlotStatus.IllegalAction | SlotStatus.SimError), env.Slots[0].LastError ?? "");
        Assert.Equal(reference.StateHash(), env.StateHash(0), "TakeRewardCard(1, 1) == ClaimReward(1), PickRewardCard(1)");
        Assert.Equal(1L, slot.Episode[EpisodeCol.StepIndex], "a macro is one decision");
        var after = env.Slots[0].Sim!;
        Assert.Equal(deck + 1, after.State.Players[0].Deck.Count);
        Assert.Equal(ShrugItOff.CardId, after.State.Players[0].Deck[^1].Id);
        Assert.Equal(RunPhase.Rewards, after.Phase);
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.ClaimReward, 0), RunAction.Of(RunActionKind.ProceedRewards) }, CandList(nb));
        Assert.True(slot.RewardChoices.ToArray().All(v => v == 0), "the claimed card reward lists no choices");
    }

    public static void Test_VecCand_RewardMacroAlternatives()
    {
        // REROLL (Driftwood's CanReroll): opens the screen, rerolls, and leaves it open with the new cards (granular rows)
        var sim = RewardsStart("MACRO-REROLL", (rs, p) => new List<Reward> { OfferedCards(rs, p, PommelStrike.CardId, ShrugItOff.CardId) });
        var reward = (CardReward)sim.State.RewardsStack[^1].Rewards[0];
        reward.CanReroll = true;
        var cands = RewardMacros.Candidates(sim);
        Assert.SequenceEqual(new[]
        {
            RunAction.Of(RewardMacros.TakeRewardCard, 0, 0), RunAction.Of(RewardMacros.TakeRewardCard, 0, 1),
            RunAction.Of(RewardMacros.TakeRewardAlternative, 0, 1), RunAction.Of(RunActionKind.ProceedRewards),
        }, cands, "Skip (alternative 0) is not offered; REROLL (alternative 1) is");
        using var nb = new NativeBuffers(1);
        var env = Observe(sim, nb);
        int w = Schema.RewardChoiceFields.Length;
        var alt = nb.Slot(0).RewardChoices.Slice(2 * w, w);
        Assert.Equal(0, alt[RcCol.RewardIndex]);
        Assert.Equal(1, alt[RcCol.Option]);
        Assert.Equal(Vocab.CardRewardAlt(CardRewardAlternative.RerollId), alt[RcCol.AltId]);
        Assert.True(alt[RcCol.Card..].ToArray().All(v => v == 0), "an alternative row has no card columns");
        Assert.Equal(2, CandRow(nb, 0, 2)[CandCol.OptionIndex]);
        Assert.Equal(0, CandRow(nb, 0, 2)[CandCol.CardId]);
        var reference = sim.Clone();
        reference.Step(RunAction.Of(RunActionKind.ClaimReward, 0));
        reference.Step(RunAction.Of(RunActionKind.RewardAlternative, 1));
        env.Step(new[] { 2 }, nb.Table);
        Assert.Equal(reference.StateHash(), env.StateHash(0), "TakeRewardAlternative(0, 1) == ClaimReward(0), RewardAlternative(1)");
        var rerolled = env.Slots[0].Sim!;
        Assert.Equal(RunPhase.CardReward, rerolled.Phase, "after a REROLL the card screen stays open");
        var granular = CandList(nb);
        Assert.True(granular.Count(a => a.Kind == RunActionKind.PickRewardCard) == RunLegalActionGenerator.OpenCardReward(rerolled.State, rerolled.Loop)!.Cards.Count
                    && granular.Contains(RunAction.Of(RunActionKind.RewardAlternative, 0)), "the open screen lists its cards and Skip");
        Assert.False(granular.Any(a => RewardMacros.IsMacro(a.Kind)), "no macros off the rewards screen");

        // SACRIFICE (Pael's Wing): completes the reward without a card
        var wing = RewardsStart("MACRO-WING", (rs, p) => new List<Reward> { OfferedCards(rs, p, PommelStrike.CardId), new GoldReward(10, p) });
        var relic = (PaelsWing)RelicCmd.Obtain(wing.State, RelicRegistry.Create(PaelsWing.RelicId), wing.State.Players[0]);
        cands = RewardMacros.Candidates(wing);
        Assert.SequenceEqual(new[]
        {
            RunAction.Of(RewardMacros.TakeRewardCard, 0, 0), RunAction.Of(RewardMacros.TakeRewardAlternative, 0, 1),
            RunAction.Of(RunActionKind.ClaimReward, 1), RunAction.Of(RunActionKind.ProceedRewards),
        }, cands);
        using var wb = new NativeBuffers(1);
        var wenv = Observe(wing, wb);
        Assert.Equal(Vocab.CardRewardAlt(PaelsWing.SacrificeAlternativeKey), wb.Slot(0).RewardChoices[1 * w + RcCol.AltId]);
        var wref = wing.Clone();
        wref.Step(RunAction.Of(RunActionKind.ClaimReward, 0));
        wref.Step(RunAction.Of(RunActionKind.RewardAlternative, 1));
        wenv.Step(new[] { 1 }, wb.Table);
        Assert.Equal(wref.StateHash(), wenv.StateHash(0));
        var sacrificed = wenv.Slots[0].Sim!;
        Assert.True(sacrificed.State.RewardsStack[^1].Rewards[0].SuccessfullySelected, "SACRIFICE completes the card reward");
        Assert.Equal(1, sacrificed.State.Players[0].Relics.OfType<PaelsWing>().Single().RewardsSacrificed);
        Assert.Equal(0, relic.RewardsSacrificed, "the env stepped its own clone");
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.ClaimReward, 1), RunAction.Of(RunActionKind.ProceedRewards) }, CandList(wb));
    }

    public static void Test_VecCand_RewardPotionClaimNeedsAFreeSlot()
    {
        static RunSim PotionScreen(string seed) => RewardsStart(seed, (rs, p) => new List<Reward>
        {
            new GoldReward(10, p), new PotionReward(PotionRegistry.Create("FIRE_POTION"), p),
        });
        static void FillBelt(PlayerState p)
        {
            for (int s = 0; s < p.PotionSlots.Length; s++)
            {
                if (p.PotionSlots[s] == null)
                {
                    Assert.True(p.AddPotionInternal(PotionRegistry.Create("BLOCK_POTION"), s).Success);
                }
            }
        }
        var claimPotion = RunAction.Of(RunActionKind.ClaimReward, 1);

        // a free slot: the claim is listed and takes the potion
        var free = PotionScreen("MACRO-POT1");
        Assert.True(RewardMacros.Candidates(free).Contains(claimPotion));

        // a full belt: the game refuses the claim (TooFull, the reward stays), so no candidate; RunSim still lists it
        var full = PotionScreen("MACRO-POT2");
        FillBelt(full.State.Players[0]);
        var reward = (PotionReward)full.State.RewardsStack[^1].Rewards[1];
        Assert.True(reward.ClaimWouldBeRefused(full.State));
        Assert.True(full.LegalActions().Contains(claimPotion), "Core's legal actions are unchanged");
        var cands = RewardMacros.Candidates(full);
        Assert.False(cands.Contains(claimPotion), "a refused potion claim is not a candidate");
        Assert.True(cands.Contains(RunAction.Of(RunActionKind.ClaimReward, 0)) && cands.Contains(RunAction.Of(RunActionKind.ProceedRewards)));
        var refused = full.Clone();
        refused.Step(claimPotion);
        Assert.False(refused.State.RewardsStack[^1].Rewards[1].SuccessfullySelected, "the game leaves the refused reward unclaimed");
        Assert.SequenceEqual(full.State.Players[0].PotionSlots.Select(x => x!.Id), refused.State.Players[0].PotionSlots.Select(x => x!.Id), "and the belt unchanged");
        using (var nb = new NativeBuffers(1))
        {
            Observe(full, nb);
            Assert.False(CandList(nb).Contains(claimPotion));
            Assert.Equal(Vocab.Potion("FIRE_POTION"), nb.Slot(0).Rewards[1 * 4 + 2], "the potion reward is still shown");
        }
        // after a discard the claim is back
        int discard = full.LegalActions().First(a => a.Kind == RunActionKind.DiscardPotion).A;
        full.Step(RunAction.Of(RunActionKind.DiscardPotion, discard));
        Assert.True(RewardMacros.Candidates(full).Contains(claimPotion));

        // Sozu with a full belt: the game consumes the reward without a potion (NotAllowed), so the claim stays a candidate
        var sozu = PotionScreen("MACRO-POT3");
        RelicCmd.Obtain(sozu.State, RelicRegistry.Create(Sozu.RelicId), sozu.State.Players[0]);
        FillBelt(sozu.State.Players[0]);
        Assert.False(((PotionReward)sozu.State.RewardsStack[^1].Rewards[1]).ClaimWouldBeRefused(sozu.State));
        Assert.True(RewardMacros.Candidates(sozu).Contains(claimPotion));
        sozu.Step(claimPotion);
        Assert.True(sozu.State.RewardsStack[^1].Rewards[1].SuccessfullySelected, "Sozu: the reward is consumed");
    }

    /// <summary>A macro that cannot continue after its ClaimReward fails closed as SIM_ERROR (never improvises).</summary>
    public static void Test_VecCand_RewardMacroFailsClosed()
    {
        var sim = RewardsStart("MACRO-FAIL", (rs, p) => new List<Reward> { OfferedCards(rs, p, PommelStrike.CardId, ShrugItOff.CardId) });
        Assert.Throws<InvalidOperationException>(() => RewardMacros.RequireOpened(sim, RunAction.Of(RewardMacros.TakeRewardCard, 0, 0)), "the screen is not open");
        var opened = sim.Clone();
        opened.Step(RunAction.Of(RunActionKind.ClaimReward, 0));
        RewardMacros.RequireOpened(opened, RunAction.Of(RewardMacros.TakeRewardCard, 0, 1));
        Assert.Throws<InvalidOperationException>(() => RewardMacros.RequireOpened(opened, RunAction.Of(RewardMacros.TakeRewardCard, 0, 2)), "no third card");
        Assert.Throws<InvalidOperationException>(() => RewardMacros.RequireOpened(opened, RunAction.Of(RewardMacros.TakeRewardAlternative, 0, 0)), "Skip is never a macro");
        Assert.Throws<ArgumentException>(() => RewardMacros.Expand(RunAction.Of(RunActionKind.ClaimReward, 0)));
        Assert.SequenceEqual(new[] { RunAction.Of(RunActionKind.ClaimReward, 0), RunAction.Of(RunActionKind.PickRewardCard, 1) },
            RewardMacros.ExpandAll(RunAction.Of(RewardMacros.TakeRewardCard, 0, 1)));
        Assert.Equal("TakeRewardAlternative", RewardMacros.KindName(RewardMacros.TakeRewardAlternative));

        // through the env: a fault injected into the second click ends the episode as SIM_ERROR
        using var nb = new NativeBuffers(1);
        var env = Observe(sim, nb);
        env.FaultInjector = (_, a) => a.Kind == RunActionKind.PickRewardCard ? new InvalidOperationException("injected") : null;
        env.Step(new[] { 0 }, nb.Table);
        Assert.True(((SlotStatus)nb.Slot(0).Status).HasFlag(SlotStatus.SimError), $"status {nb.Slot(0).Status}");
    }
}
