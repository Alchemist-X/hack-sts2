namespace Sts2Sim.Core.Run;

public sealed class StoneOfAllTime : EventModel
{
    public const string EventId = "STONE_OF_ALL_TIME";

    public const int DrinkMaxHpGain = 10;

    public const int PushHpLoss = 6;

    public const int PushVigorousAmount = 8;

    public const string VigorousId = "VIGOROUS";

    private int _drinkAndLiftPotionSlot = -1;

    public override string Id => EventId;

    public int DrinkAndLiftPotionSlot => _drinkAndLiftPotionSlot;

    // Option-text var (Models.Events/StoneOfAllTime.cs:292 StringVar DrinkRandomPotion = the drawn potion's title).
    public string? DrinkRandomPotion => _drinkAndLiftPotionSlot >= 0 && Owner is { } p && _drinkAndLiftPotionSlot < p.PotionSlots.Length ? p.PotionSlots[_drinkAndLiftPotionSlot]?.Id : null;

    public override bool IsAllowed(RunState runState)
    {
        if (runState.CurrentActIndex == 1)
        {
            var players = runState.Players;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].Potions.Any())
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    protected override void BeforeEventStarted(RunState rs, bool isPreFinished)
    {
        EventKit3456.OwnerOf(this).CanUseOrRemovePotions = false;
    }

    protected override void OnEventFinished()
    {
        EventKit3456.OwnerOf(this).CanUseOrRemovePotions = true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var potion = EventKit3456.RngOf(this).NextItem(owner.Potions);
        _drinkAndLiftPotionSlot = potion == null ? -1 : Array.IndexOf(owner.PotionSlots, potion);
        var lift = potion != null
            ? new EventOption("STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT", Lift)
            : new EventOption("STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT_LOCKED", null);
        var vigorous = EnchantmentRegistry.Create(VigorousId);
        var push = owner.Deck.Count(vigorous.CanEnchant) < 1
            ? new EventOption("STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH_LOCKED", null)
            : new EventOption("STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH", Push);
        return new[] { lift, push };
    }

    private void Lift(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var potion = _drinkAndLiftPotionSlot < 0 ? null : owner.GetPotionAtSlotIndex(_drinkAndLiftPotionSlot);
        if (potion != null)
        {
            PotionCmd.Discard(rs, potion);
        }
        EventKit3456.GainMaxHp(rs, owner, DrinkMaxHpGain);
        EventKit3456.RngOf(this).NextInt(100);
        SetEventFinished("STONE_OF_ALL_TIME.pages.LIFT.description");
    }

    private void Push(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.Damage(rs, owner, PushHpLoss, ValueProp.Unblockable | ValueProp.Unpowered);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, VigorousId, null, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), "StoneOfAllTime.Push", Id);
        for (int i = 0; i < selected.Count; i++)
        {
            CardCmd.Enchant(VigorousId, selected[i], PushVigorousAmount);
        }
        EventKit3456.RngOf(this).NextInt(100);
        SetEventFinished("STONE_OF_ALL_TIME.pages.PUSH.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_drinkAndLiftPotionSlot);
    }
}
