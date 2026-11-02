namespace Sts2Sim.Core.Run;

public sealed class SapphireSeed : EventModel
{
    public const string EventId = "SAPPHIRE_SEED";

    public const int Heal = 9;

    public const string SownId = "SOWN";

    public override string Id => EventId;

    // Option-text var (Models.Events/SapphireSeed.cs:213 StringVar Enchantment).
    public static string Enchantment => SownId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("SAPPHIRE_SEED.pages.INITIAL.options.EAT", Eat),
        new EventOption("SAPPHIRE_SEED.pages.INITIAL.options.PLANT", Plant)
    };

    private void Eat(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.Heal(owner, Heal);
        var selected = EventKit3456.FromDeckForUpgrade(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1), "SapphireSeed.Eat", Id);
        if (selected.Count > 0)
        {
            CardCmd.Upgrade(null, selected[0]);
        }
        SetEventFinished("SAPPHIRE_SEED.pages.EAT.description");
    }

    private void Plant(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, SownId, null, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), "SapphireSeed.Plant", Id);
        if (selected.Count > 0)
        {
            CardCmd.Enchant(SownId, selected[0], 1m);
        }
        SetEventFinished("SAPPHIRE_SEED.pages.PLANT.description");
    }
}
