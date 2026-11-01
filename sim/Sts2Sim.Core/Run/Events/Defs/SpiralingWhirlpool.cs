namespace Sts2Sim.Core.Run;

public sealed class SpiralingWhirlpool : EventModel
{
    public const string EventId = "SPIRALING_WHIRLPOOL";

    public const decimal HealFraction = 0.33m;

    public const string SpiralId = "SPIRAL";

    private decimal _heal;

    public override string Id => EventId;

    public decimal HealAmount => _heal;

    public override void CalculateVars(RunState rs)
    {
        _heal = Owner != null ? Owner.MaxHp * HealFraction : 0m;
    }

    public override bool IsAllowed(RunState runState)
    {
        var spiral = EnchantmentRegistry.Create(SpiralId);
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].Deck.Any(spiral.CanEnchant))
            {
                return false;
            }
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("SPIRALING_WHIRLPOOL.pages.INITIAL.options.OBSERVE", ObserveTheSpiral),
        new EventOption("SPIRALING_WHIRLPOOL.pages.INITIAL.options.DRINK", Drink)
    };

    private void ObserveTheSpiral(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var spiral = EnchantmentRegistry.Create(SpiralId);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, SpiralId, spiral.CanEnchant, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), "SpiralingWhirlpool.ObserveTheSpiral", Id);
        if (selected.Count > 0)
        {
            CardCmd.Enchant(SpiralId, selected[0], 1m);
        }
        SetEventFinished("SPIRALING_WHIRLPOOL.pages.OBSERVE.description");
    }

    private void Drink(RunState rs)
    {
        EventKit3456.Heal(EventKit3456.OwnerOf(this), (int)_heal);
        SetEventFinished("SPIRALING_WHIRLPOOL.pages.DRINK.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_heal);
    }
}
