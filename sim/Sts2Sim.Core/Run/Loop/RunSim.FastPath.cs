namespace Sts2Sim.Core.Run;

/// <summary>
/// Clone-free stepping behind a static prompt-site predicate (CONTRACTS.md §B, "fast path").
///
/// <see cref="Step(RunAction)"/> deep-clones the run before every action so that a choice prompt can be replayed from
/// that basis with the answer tape. When <see cref="MayPrompt"/> proves that the action cannot reach
/// <see cref="ChoiceRuntime.Ask"/>, <see cref="StepFast"/> skips the clone and runs
/// <see cref="Step(RunAction, IChoiceAnswerer)"/> with <see cref="FastPathAnswerer"/>, which throws if a prompt appears
/// anyway. Both paths give the same state (same <see cref="StateHash"/>) whenever no prompt occurs: the only difference
/// is the clone. The predicate is conservative: it may say "may prompt" for an action that does not, never the reverse
/// (the reverse is a bug, surfaced as <see cref="FastPathPromptException"/>, and the run is poisoned).
/// </summary>
public sealed partial class RunSim
{
    public const string BlockPending = "pending-prompt";
    public const string BlockTerminal = "terminal";
    public const string BlockEventRoom = "event-room";
    public const string BlockModifier = "modifier";
    public const string BlockRelic = "relic";
    public const string BlockPotion = "potion";
    public const string BlockCard = "card";
    public const string BlockCreature = "creature";
    public const string BlockSly = "sly-card";
    public const string BlockPlayedCard = "played-card";
    public const string BlockUsedPotion = "used-potion";
    public const string BlockTravel = "travel";
    public const string BlockReward = "reward";
    public const string BlockRoomAction = "room-action";
    public const string BlockAction = "action";

    private bool _poisoned;

    /// <summary>
    /// False only when <paramref name="action"/> provably cannot reach a choice prompt from the current state, using the
    /// generated table <see cref="PromptSites"/>. Conservative rules:
    /// <list type="bullet">
    /// <item>a pending prompt; out of combat, an event room anywhere on the room stack (this covers the event phase): true;</item>
    /// <item>any "presence" hazard alive: a run modifier, an owned relic/potion, a card in the deck or a combat pile (or its
    /// enchantment/affliction), a power on any creature, a monster, an orb: true;</item>
    /// <item>combat: playing a prompt-capable card, using a prompt-capable potion, or any prompt-capable card that is Sly
    /// this turn (the kernel auto-plays a discarded Sly card): true;</item>
    /// <item>out of combat: travel to anything but a shop, rest site or treasure; claiming any reward but gold, a potion or
    /// opening a card reward; a reward alternative; closing a terminal rewards screen after a boss / with a continuation
    /// relic; buying; choosing a rest option; taking a treasure relic; using a prompt-capable potion: true.</item>
    /// </list>
    /// </summary>
    public bool MayPrompt(RunAction action) => FastPathBlocker(action) != null;

    /// <summary>Null when the fast path is safe for <paramref name="action"/>; otherwise a short reason (one of the Block* constants).</summary>
    public string? FastPathBlocker(RunAction action)
    {
        if (_pending != null)
        {
            return BlockPending;
        }
        if (IsTerminal || _poisoned)
        {
            return BlockTerminal;
        }
        var rs = _state;
        var phase = _loop.Phase;
        if (phase != RunPhase.Combat)
        {
            // Out of combat an event room anywhere on the stack can resume (after its combat's rewards) or take options.
            // Inside a combat it cannot act: events are not hook listeners, and a combat room's victory always offers a
            // rewards set (RewardsSet.Offer pushes for a CombatRoom), so the room stack only moves on a later
            // ProceedRewards step.
            var rooms = rs.CurrentRooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i] is EventRoom)
                {
                    return BlockEventRoom;
                }
            }
        }
        if (PresenceBlocker(rs) is { } presence)
        {
            return presence;
        }
        return phase == RunPhase.Combat ? CombatBlocker(rs, action) : OutOfCombatBlocker(rs, action);
    }

    /// <summary>
    /// Steps on the clone-free path when <see cref="MayPrompt"/> is false, else through <see cref="Step(RunAction)"/>.
    /// Returns true when the fast path ran. A prompt on the fast path throws <see cref="FastPathPromptException"/> and
    /// poisons the run (it is mid-action; every later step throws).
    /// </summary>
    public bool StepFast(RunAction action)
    {
        ThrowIfPoisoned();
        if (MayPrompt(action))
        {
            Step(action);
            return false;
        }
        StepFastUnchecked(action);
        return true;
    }

    /// <summary>
    /// The clone-free step without consulting <see cref="MayPrompt"/> (tests and diagnostics: it is how the fail-closed
    /// path is exercised). A prompt throws <see cref="FastPathPromptException"/> and poisons the run.
    /// </summary>
    public void StepFastUnchecked(RunAction action)
    {
        try
        {
            Step(action, FastPathAnswerer.Instance);
        }
        catch (FastPathPromptException ex)
        {
            _poisoned = true;
            throw new FastPathPromptException(ex.Prompt, action);
        }
    }

    /// <summary>True after a fast-path step hit a prompt: the run is mid-action and unusable.</summary>
    public bool IsPoisoned => _poisoned;

    private void ThrowIfPoisoned()
    {
        if (_poisoned)
        {
            throw new InvalidOperationException("This run was left mid-action by a fast-path step that reached a choice prompt; it cannot be stepped.");
        }
    }

    private static string? PresenceBlocker(RunState rs)
    {
        var modifiers = rs.Modifiers;
        for (int i = 0; i < modifiers.Count; i++)
        {
            if (PromptSites.Modifiers.Contains(modifiers[i].Id))
            {
                return BlockModifier;
            }
        }
        var players = rs.Players;
        for (int p = 0; p < players.Count; p++)
        {
            var player = players[p];
            var relics = player.Relics;
            for (int i = 0; i < relics.Count; i++)
            {
                if (PromptSites.RelicsPresence.Contains(relics[i].Id))
                {
                    return BlockRelic;
                }
            }
            var slots = player.PotionSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } potion && PromptSites.PotionsPresence.Contains(potion.Id))
                {
                    return BlockPotion;
                }
            }
            var deck = player.Deck;
            for (int i = 0; i < deck.Count; i++)
            {
                if (PromptSites.IsPresenceCard(deck[i]))
                {
                    return BlockCard;
                }
            }
        }
        var cs = rs.Combat;
        if (cs == null)
        {
            return null;
        }
        var piles = cs.Player.AllPiles;
        for (int k = 0; k < piles.Count; k++)
        {
            var cards = piles[k].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (PromptSites.IsPresenceCard(card))
                {
                    return BlockCard;
                }
                // A discarded Sly card is auto-played by the kernel (CardCmd.Play.cs Discard); static Sly or single-turn
                // Sly only (hook-granted Sly comes from presence hazards, e.g. MASTER_PLANNER_POWER).
                if (card.IsSlyThisTurn && PromptSites.CardsOnPlay.Contains(card.Id))
                {
                    return BlockSly;
                }
            }
        }
        var orbs = cs.Player.OrbQueue.Orbs;
        for (int i = 0; i < orbs.Count; i++)
        {
            if (PromptSites.Orbs.Contains(orbs[i].Id))
            {
                return BlockCreature;
            }
        }
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (CreatureBlocks(allies[i]))
            {
                return BlockCreature;
            }
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CreatureBlocks(monsters[i]))
            {
                return BlockCreature;
            }
        }
        return null;
    }

    private static bool CreatureBlocks(CreatureState creature)
    {
        if (creature is MonsterState monster && PromptSites.Monsters.Contains(monster.MonsterId))
        {
            return true;
        }
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (PromptSites.Powers.Contains(powers[i].Id))
            {
                return true;
            }
        }
        return false;
    }

    private static string? CombatBlocker(RunState rs, RunAction action)
    {
        var cs = rs.Combat;
        if (cs == null)
        {
            return BlockAction;
        }
        switch (action.Kind)
        {
            case RunActionKind.PlayCard:
            {
                var hand = cs.Player.Hand;
                if ((uint)action.A >= (uint)hand.Count)
                {
                    return BlockAction;
                }
                return PromptSites.IsPlayCard(hand[action.A]) ? BlockPlayedCard : null;
            }
            case RunActionKind.UsePotion:
                return UsePotionBlocker(cs.Player, action.A);
            case RunActionKind.EndTurn:
            case RunActionKind.DiscardPotion:
                return null;
            default:
                return BlockAction;
        }
    }

    private static string? UsePotionBlocker(PlayerState player, int slot)
    {
        var slots = player.PotionSlots;
        if ((uint)slot >= (uint)slots.Length)
        {
            return BlockAction;
        }
        return slots[slot] is { } potion && PromptSites.IsUsePotion(potion) ? BlockUsedPotion : null;
    }

    private static string? OutOfCombatBlocker(RunState rs, RunAction action)
    {
        switch (action.Kind)
        {
            case RunActionKind.Travel:
                return TravelBlocker(rs, action.A);
            case RunActionKind.ClaimReward:
            {
                var set = RunLegalActionGenerator.TopRewards(rs);
                if (set == null || (uint)action.A >= (uint)set.Rewards.Count)
                {
                    return BlockAction;
                }
                // Gold, a potion, or opening a card reward cannot prompt; a relic (AfterObtained), a card removal, a
                // special card reward or anything newer may.
                return set.Rewards[action.A] is GoldReward or PotionReward or CardReward ? null : BlockReward;
            }
            case RunActionKind.ProceedRewards:
            {
                var set = RunLegalActionGenerator.TopRewards(rs);
                if (set == null)
                {
                    return BlockAction;
                }
                if (set.ContinuationRelicId != null)
                {
                    return BlockReward;
                }
                // Closing a terminal screen after a boss (or in the victory room) moves to the next act / the final event.
                if (set.IsTerminal && RoomStack.CurrentRoom(rs) is { } room && (room.RoomType == RoomType.Boss || room.IsVictoryRoom))
                {
                    return BlockReward;
                }
                return null;
            }
            case RunActionKind.PickRewardCard:
                return null;
            case RunActionKind.RewardAlternative:
                return BlockReward;
            case RunActionKind.LeaveShop:
            case RunActionKind.LeaveRestSite:
            case RunActionKind.OpenChest:
            case RunActionKind.SkipTreasureRelic:
            case RunActionKind.DiscardPotion:
                return null;
            case RunActionKind.BuyEntry:
            case RunActionKind.ChooseRestOption:
            case RunActionKind.TakeTreasureRelic:
                return BlockRoomAction;
            case RunActionKind.UsePotion:
            {
                var players = rs.Players;
                return players.Count == 0 ? BlockAction : UsePotionBlocker(players[0], action.A);
            }
            default:
                return BlockAction;
        }
    }

    private static string? TravelBlocker(RunState rs, int index)
    {
        var points = new List<MapPoint>(8);
        RunLegalActionGenerator.GetLegalMapPoints(rs, points);
        if ((uint)index >= (uint)points.Count)
        {
            return BlockAction;
        }
        // Combat rooms start a combat in this step (turn-1 hooks of monsters not yet known); unknown rooms may be
        // events; ancients are events. Shops, rest sites and treasure rooms only open.
        return points[index].PointType is MapPointType.Shop or MapPointType.RestSite or MapPointType.Treasure ? null : BlockTravel;
    }
}
