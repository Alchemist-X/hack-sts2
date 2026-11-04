using Sts2Sim.Core;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// Candidate rows (owner: V2). Row i describes <c>legal[i]</c>, the Vec candidate list (<see cref="RewardMacros.Fill"/>:
/// LegalActions() order, with the rewards-screen macro rows; CONTRACTS.md §D4/§D3.1 #25):
/// kind, A, B, hand_slot, target_slot, potion_slot, option_index, card_id, target_uid. Returns the number of rows written
/// (min(legal.Count, CAP_CANDIDATES)); only those indices are selectable by <c>step</c>.
/// <para>
/// Column conventions: kind/A/B are the <see cref="RunAction"/> fields verbatim (<see cref="ToAction"/> inverts them).
/// Pointer columns are -1 when they do not apply: hand_slot = A for PlayCard; target_slot = B for PlayCard/UsePotion when
/// B &gt;= 0 (index into <c>enemies</c>), else -1 (B = -1 self/none, B &lt;= -3 pet <c>Allies[-2 - B]</c>: read B);
/// potion_slot = A for UsePotion/DiscardPotion; option_index = A for Travel, ClaimReward, PickRewardCard,
/// RewardAlternative, BuyEntry, ChooseRestOption, ChooseEventOption and PromptSelect, and the <c>reward_choices</c> row
/// for the macro kinds TakeRewardCard / TakeRewardAlternative (A = rewards row, B = card / alternative index). card_id
/// (0 = none) is the cards vocab id of the played hand card, of a card-reward pick (PickRewardCard, TakeRewardCard), or of
/// a prompt option that is a card. target_uid (0 = none) is the target monster's CombatId (= enemies.uid). Rows at or
/// beyond the returned count are zero.
/// </para>
/// </summary>
public static class CandidateTable
{
    public static int Fill(RunSim sim, IReadOnlyList<RunAction> legal, Span<int> cand, out bool overflow)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(legal);
        int width = Schema.CandFields.Length;
        int cap = Math.Min(Caps.Candidates, cand.Length / width);
        int n = Math.Min(legal.Count, cap);
        overflow = legal.Count > cap;
        if (n < cap)
        {
            cand[(n * width)..(cap * width)].Clear();
        }
        if (n == 0)
        {
            return 0;
        }

        var rs = sim.State;
        var cs = rs.Combat;
        var prompt = sim.PendingChoice;
        int macroRow = 0;
        for (int i = 0; i < n; i++)
        {
            var a = legal[i];
            var row = cand.Slice(i * width, width);
            row[CandCol.Kind] = (int)a.Kind;
            row[CandCol.A] = a.A;
            row[CandCol.B] = a.B;
            int handSlot = -1, targetSlot = -1, potionSlot = -1, option = -1, cardId = 0, targetUid = 0;
            if (RewardMacros.IsMacro(a.Kind))
            {
                // the k-th macro row targets reward_choices row k (RewardMacros.Fill emits them in choice order)
                option = macroRow++;
                if (a.Kind == RewardMacros.TakeRewardCard)
                {
                    cardId = UnopenedRewardCardId(rs, a.A, a.B);
                }
            }
            else switch (a.Kind)
            {
                case RunActionKind.PlayCard:
                    handSlot = a.A;
                    if (cs != null && (uint)a.A < (uint)cs.Player.Hand.Count)
                    {
                        cardId = Vocab.Card(cs.Player.Hand[a.A].Id);
                    }
                    TargetOf(cs, a.B, ref targetSlot, ref targetUid);
                    break;
                case RunActionKind.UsePotion:
                    potionSlot = a.A;
                    TargetOf(cs, a.B, ref targetSlot, ref targetUid);
                    break;
                case RunActionKind.DiscardPotion:
                    potionSlot = a.A;
                    break;
                case RunActionKind.PickRewardCard:
                    option = a.A;
                    cardId = RewardCardId(sim, a.A);
                    break;
                case RunActionKind.PromptSelect:
                    option = a.A;
                    if (prompt != null && (uint)a.A < (uint)prompt.Options.Count)
                    {
                        cardId = PromptCardId(prompt, prompt.Options[a.A]);
                    }
                    break;
                case RunActionKind.Travel:
                case RunActionKind.ClaimReward:
                case RunActionKind.RewardAlternative:
                case RunActionKind.BuyEntry:
                case RunActionKind.ChooseRestOption:
                case RunActionKind.ChooseEventOption:
                    option = a.A;
                    break;
            }
            row[CandCol.HandSlot] = handSlot;
            row[CandCol.TargetSlot] = targetSlot;
            row[CandCol.PotionSlot] = potionSlot;
            row[CandCol.OptionIndex] = option;
            row[CandCol.CardId] = cardId;
            row[CandCol.TargetUid] = targetUid;
        }
        return n;
    }

    /// <summary>Fills the rows of <paramref name="sim"/>'s Vec candidate list (<see cref="RewardMacros.Candidates"/>).</summary>
    public static int Fill(RunSim sim, Span<int> cand, out bool overflow) => Fill(sim, RewardMacros.Candidates(sim), cand, out overflow);

    /// <summary>
    /// The candidate a row stands for: (kind, A, B) of the row. A macro kind (TakeRewardCard / TakeRewardAlternative) comes
    /// back as is; <see cref="RewardMacros.ExpandAll"/> gives the game actions it runs.
    /// </summary>
    public static RunAction ToAction(ReadOnlySpan<int> row)
    {
        if (row.Length < Schema.CandFields.Length)
        {
            throw new ArgumentException($"a candidate row has {Schema.CandFields.Length} columns, got {row.Length}", nameof(row));
        }
        int kind = row[CandCol.Kind];
        if (kind < 0 || kind >= RewardMacros.KindCount)
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"candidate kind {kind} is not a RunActionKind or a reward macro kind");
        }
        return new RunAction((RunActionKind)kind, row[CandCol.A], row[CandCol.B]);
    }

    private static void TargetOf(Core.State.CombatState? cs, int b, ref int targetSlot, ref int targetUid)
    {
        if (b < 0)
        {
            return;
        }
        targetSlot = b;
        if (cs != null && b < cs.Monsters.Count)
        {
            targetUid = cs.Monsters[b].CombatId;
        }
    }

    /// <summary>The cards vocab id of card <paramref name="card"/> of the (unopened) card reward in rewards row <paramref name="reward"/>.</summary>
    private static int UnopenedRewardCardId(RunState rs, int reward, int card)
    {
        var rewards = RunLegalActionGenerator.TopRewards(rs)?.Rewards;
        if (rewards == null || (uint)reward >= (uint)rewards.Count || rewards[reward] is not CardReward cr || (uint)card >= (uint)cr.Cards.Count)
        {
            return 0;
        }
        return Vocab.Card(cr.Cards[card].Card.Id);
    }

    private static int RewardCardId(RunSim sim, int index)
    {
        var cards = RunLegalActionGenerator.OpenCardReward(sim.State, sim.Loop)?.Cards;
        if (cards == null || (uint)index >= (uint)cards.Count)
        {
            return 0;
        }
        return Vocab.Card(cards[index].Card.Id);
    }

    /// <summary>The cards vocab id of a prompt option that is a card (0 for relic/potion/bundle/index options).</summary>
    private static int PromptCardId(ChoicePrompt prompt, ChoiceOption option)
    {
        if (prompt.Kind == ChoicePromptKind.Bundle || string.IsNullOrEmpty(option.ModelId))
        {
            return 0;
        }
        int id = Vocab.Card(option.ModelId);
        if (id < 2)
        {
            return 0;
        }
        if (prompt.ResultType == ChoiceResultType.CombatCard || prompt.Kind != ChoicePromptKind.RewardsGrid && prompt.Kind != ChoicePromptKind.SimpleGrid)
        {
            return id;
        }
        // Grid prompts can mix item kinds: only a model id that is not also a relic/potion id counts as a card.
        return Vocab.Relic(option.ModelId) >= 2 || Vocab.Potion(option.ModelId) >= 2 ? 0 : id;
    }
}
