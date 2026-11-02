namespace Sts2Sim.Core.Run;

public sealed class Symbiote : EventModel
{
    public const string EventId = "SYMBIOTE";

    public const int Cards = 1;

    public const string ApproachSite = "SYMBIOTE:APPROACH";

    public const string KillWithFireSite = "SYMBIOTE:KILL_WITH_FIRE";

    public override string Id => EventId;

    // Option-text var (Models.Events/Symbiote.cs:224 StringVar Enchantment).
    public static string Enchantment => "CORRUPTED";

    public override bool IsAllowed(RunState rs) => rs.CurrentActIndex > 0;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = Owner ?? throw new InvalidOperationException("Symbiote has no owner.");
        var corrupted = new Corrupted();
        var approach = owner.Deck.Any(corrupted.CanEnchant)
            ? new EventOption(InitialOptionKey("APPROACH"), Approach)
            : new EventOption(InitialOptionKey("APPROACH_LOCKED"), null);
        return new[]
        {
            approach,
            new EventOption(InitialOptionKey("KILL_WITH_FIRE"), KillWithFire)
        };
    }

    private void Approach(RunState rs)
    {
        var owner = Owner!;
        var picked = CardSelectCmd.FromDeckForEnchantment(rs, owner, new Corrupted(), 1, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), new ChoiceSource(ApproachSite, Id, ChoiceSource.NoInstance));
        var card = picked.FirstOrDefault();
        if (card != null)
        {
            CardCmd.Enchant<Corrupted>(card, 1m);
        }
        SetEventFinished(Id + ".pages.APPROACH.description");
    }

    private void KillWithFire(RunState rs)
    {
        var owner = Owner!;
        var picked = CardSelectCmd.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, Cards), new ChoiceSource(KillWithFireSite, Id, ChoiceSource.NoInstance)).ToList();
        foreach (var card in picked)
        {
            CardCmd.TransformToRandom(rs, card, Rng!);
        }
        SetEventFinished(Id + ".pages.KILL_WITH_FIRE.description");
    }
}
