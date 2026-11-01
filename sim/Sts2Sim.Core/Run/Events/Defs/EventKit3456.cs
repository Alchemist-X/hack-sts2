namespace Sts2Sim.Core.Run;

internal static class EventKit3456
{
    public const string UnportedRelicSite = "EventKit3456: relic not ported; obtain skipped";

    public const string UnportedPotionSite = "EventKit3456: potion not ported; potion reward skipped";

    public const string UnportedTransformOptionsSite = "EventKit3456.TransformToRandomInDeck: unported transform options dropped from the draw";

    public static PlayerState OwnerOf(EventModel ev) => ev.Owner ?? throw new InvalidOperationException("Event " + ev.Id + " has no owner.");

    public static Rng RngOf(EventModel ev) => ev.Rng ?? throw new InvalidOperationException("Event " + ev.Id + " has no rng.");

    public static void ObtainRelic(RunState rs, string relicId, PlayerState player)
    {
        if (!RelicRegistry.Models.Contains(relicId))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedRelicSite + " (" + relicId + ")");
            return;
        }
        RelicCmd.Obtain(rs, relicId, player);
    }

    public static PotionModel? CreatePotion(RunState rs, string potionId)
    {
        if (!PotionRegistry.Models.Contains(potionId))
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedPotionSite + " (" + potionId + ")");
            return null;
        }
        return PotionRegistry.Create(potionId);
    }

    public static CardInstance CreateCard(RunState rs, string cardId, PlayerState player) => CardFactory.CreateRunCard(rs, CardRegistry.Get(cardId), player);

    public static void Heal(PlayerState player, decimal amount) => CreatureInternals.HealInternal(player, amount);

    public static void GainMaxHp(RunState rs, PlayerState player, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use LoseMaxHp for max HP loss.");
        }
        decimal gained = SetMaxHp(rs, player, player.MaxHp + amount);
        Heal(player, gained);
    }

    public static void LoseMaxHp(RunState rs, PlayerState player, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use GainMaxHp for max HP gain.");
        }
        decimal newMaxHp = player.MaxHp - amount;
        if (newMaxHp < player.CurrentHp)
        {
            Damage(rs, player, player.CurrentHp - newMaxHp, ValueProp.Unblockable | ValueProp.Unpowered);
        }
        SetMaxHp(rs, player, Math.Max(1.0m, newMaxHp));
    }

    private static decimal SetMaxHp(RunState rs, PlayerState player, decimal amount)
    {
        int oldMaxHp = player.MaxHp;
        CreatureInternals.SetMaxHpInternal(player, Math.Max(0m, amount));
        int newMaxHp = player.MaxHp;
        if (player.MaxHp <= 0)
        {
            Kill(rs, player, 0);
        }
        return newMaxHp - oldMaxHp;
    }

    public static DamageResult Damage(RunState rs, PlayerState target, decimal amount, ValueProp props)
    {
        if (target.IsDead)
        {
            return new DamageResult(target, props);
        }
        List<AbstractModel>? modifiers = null;
        decimal modifiedAmount = Hook.ModifyDamage(rs, null, target, null, amount, props, null, null, ModifyDamageHookType.All, ref modifiers);
        Hook.AfterModifyingDamageAmount(rs, null, null, modifiers);
        Hook.BeforeDamageReceived(rs, null, target, modifiedAmount, props, null, null);
        decimal blockedDamage = CreatureInternals.DamageBlockInternal(target, modifiedAmount, props);
        modifiers = null;
        decimal unblockedDamage = Hook.ModifyHpLost(rs, null, target, Math.Max(modifiedAmount - blockedDamage, 0m), props, null, null, HpLossHookPhase.BeforeOsty, ref modifiers);
        Hook.AfterModifyingHpLostBeforeOsty(rs, null, modifiers);
        modifiers = null;
        unblockedDamage = Hook.ModifyHpLost(rs, null, target, unblockedDamage, props, null, null, HpLossHookPhase.AfterOsty, ref modifiers);
        Hook.AfterModifyingHpLostAfterOsty(rs, null, modifiers);
        var result = CreatureInternals.LoseHpInternal(target, unblockedDamage, props);
        result.BlockedDamage = (int)blockedDamage;
        result.WasBlockBroken = target.Block <= 0 && blockedDamage > 0m;
        result.WasFullyBlocked = (props & ValueProp.Unblockable) == 0 && (blockedDamage > 0m || target.Block > 0) && (int)unblockedDamage == 0;
        if (result.UnblockedDamage > 0)
        {
            Hook.AfterCurrentHpChanged(rs, null, target, -result.UnblockedDamage);
        }
        if (!result.WasTargetKilled || !target.IsDead)
        {
            Hook.AfterDamageReceived(rs, null, target, result, props, null, null);
        }
        else
        {
            Kill(rs, target, 0);
        }
        return result;
    }

    private static void Kill(RunState rs, PlayerState player, int recursion)
    {
        int currentHp = player.CurrentHp;
        if (currentHp > 0)
        {
            CreatureInternals.LoseHpInternal(player, currentHp, ValueProp.Unblockable | ValueProp.Unpowered);
            Hook.AfterCurrentHpChanged(rs, null, player, -currentHp);
        }
        Hook.BeforeDeath(rs, null, player);
        AbstractModel? preventer = null;
        if (player.MaxHp <= 0 || Hook.ShouldDie(rs, null, player, out preventer))
        {
            Hook.AfterDeath(rs, null, player, false);
            player.OrbQueue.Clear();
            player.DeactivateHooks();
            return;
        }
        if (recursion >= 10)
        {
            throw new InvalidOperationException("Combat is ending, but something is continually preventing the last creature from being killed!");
        }
        Hook.AfterDeath(rs, null, player, true);
        Hook.AfterPreventingDeath(rs, null, preventer!, player);
        if (player.IsDead)
        {
            Kill(rs, player, recursion + 1);
        }
    }

    public static CardInstance TransformToRandomInDeck(RunState rs, CardInstance original, Rng rng)
    {
        if (!original.IsTransformable(null))
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable.");
        }
        if (!original.IsInDeck)
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it has no pile.");
        }
        var options = new List<CardDef>(CardFactory.GetDefaultTransformationOptions(rs, original, false));
        int unported = options.RemoveAll(static c => c is UnportedCardDef);
        if (unported > 0)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedTransformOptionsSite);
        }
        if (options.Count == 0)
        {
            throw new NotImplementedException("W3 gap: no ported transform option for " + original.Id);
        }
        var owner = original.Owner;
        var replacement = CardFactory.CreateRunCard(rs, rng.NextItem((IReadOnlyList<CardDef>)options), owner);
        original.RemoveFromCurrentPile(null);
        replacement = Hook.ModifyCardBeingAddedToDeck(rs, replacement);
        owner.Deck.Add(replacement);
        Hook.AfterCardChangedPiles(rs, null, replacement, PileType.Deck, null);
        original.Def.AfterTransformedFrom(null, original);
        replacement.Def.AfterTransformedTo(null, replacement);
        original.RemoveFromState(null);
        return replacement;
    }

    public static IReadOnlyList<CardInstance> FromDeckForUpgrade(RunState rs, PlayerState player, CardSelectorPrefs prefs, string siteId, string sourceId)
        => RoomPrompts.FromDeckForUpgrade(rs, player, prefs, siteId, sourceId);

    public static IReadOnlyList<CardInstance> FromDeckForRemoval(RunState rs, PlayerState player, CardSelectorPrefs prefs, string siteId, string sourceId, Func<CardInstance, bool>? filter = null)
        => RoomPrompts.FromDeckForRemoval(rs, player, prefs, siteId, sourceId, filter);

    public static IReadOnlyList<CardInstance> FromDeckForTransformation(RunState rs, PlayerState player, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        var cards = new List<CardInstance>();
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].Type != CardType.Quest && deck[i].IsTransformable(null))
            {
                cards.Add(deck[i]);
            }
        }
        if (cards.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (cards.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return cards;
        }
        return Select(rs, ChoicePromptKind.DeckTransform, PileType.Deck, cards, prefs, siteId, sourceId);
    }

    public static IReadOnlyList<CardInstance> FromDeckForEnchantment(RunState rs, PlayerState player, string enchantmentId, Func<CardInstance, bool>? additionalFilter, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        var enchantment = EnchantmentRegistry.Create(enchantmentId);
        var cards = new List<CardInstance>();
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (enchantment.CanEnchant(deck[i]) && (additionalFilter == null || additionalFilter(deck[i])))
            {
                cards.Add(deck[i]);
            }
        }
        if (cards.Count <= prefs.MinSelect)
        {
            return cards;
        }
        if (player.IsDead)
        {
            return Array.Empty<CardInstance>();
        }
        return Select(rs, ChoicePromptKind.DeckEnchant, PileType.Deck, cards, prefs, siteId, sourceId);
    }

    public static void SelectCardsToAddToDeckFromGrid(RunState rs, PlayerState player, List<CardCreationResult> cards, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        IReadOnlyList<CardInstance> selected;
        if (cards.Count == 0)
        {
            selected = Array.Empty<CardInstance>();
        }
        else
        {
            var options = new List<CardInstance>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                options.Add(cards[i].Card);
            }
            selected = !prefs.RequireManualConfirmation && options.Count <= prefs.MinSelect
                ? options
                : Select(rs, ChoicePromptKind.SimpleGrid, null, options, prefs, siteId, sourceId);
        }
        for (int i = 0; i < selected.Count; i++)
        {
            CardPileCmd.Add(rs, selected[i], PileType.Deck);
        }
    }

    private static IReadOnlyList<CardInstance> Select(RunState rs, ChoicePromptKind kind, PileType? pile, List<CardInstance> cards, CardSelectorPrefs prefs, string siteId, string sourceId)
    {
        var options = new ChoiceOption[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            options[i] = new ChoiceOption(cards[i].InstanceId, cards[i].Id, cards[i].UpgradeLevel);
        }
        int max = Math.Min(prefs.MaxSelect, cards.Count);
        int min = Math.Min(prefs.MinSelect, max);
        var prompt = new ChoicePrompt(kind, siteId, sourceId, -1, pile, options, min, max, prefs.RequireManualConfirmation, false, prefs.Cancelable, false, ChoiceResultType.Index);
        var answer = rs.Choice.Ask(prompt);
        if (answer.Cancelled || answer.Picks.Count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        var picked = new List<CardInstance>(answer.Picks.Count);
        for (int i = 0; i < answer.Picks.Count; i++)
        {
            picked.Add(cards[answer.Picks[i]]);
        }
        return picked;
    }
}
