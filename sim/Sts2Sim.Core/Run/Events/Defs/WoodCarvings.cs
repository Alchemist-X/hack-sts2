namespace Sts2Sim.Core.Run;

public sealed class WoodCarvings : EventModel
{
    public const string EventId = "WOOD_CARVINGS";

    public const string PeckId = "PECK";

    public const string ToricToughnessId = "TORIC_TOUGHNESS";

    public const string BirdSite = "WOOD_CARVINGS:BIRD";

    public const string SnakeSite = "WOOD_CARVINGS:SNAKE";

    public const string TorusSite = "WOOD_CARVINGS:TORUS";

    public override string Id => EventId;

    // Option-text vars (Models.Events/WoodCarvings.cs:305 StringVar BirdCard / SnakeEnchantment / ToricCard).
    public static string BirdCard => PeckId;

    public static string SnakeEnchantment => "SLITHER";

    public static string ToricCard => ToricToughnessId;

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.Deck.Any(c => c != null && c.Rarity == CardRarity.Basic && c.IsRemovable));

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var slither = new Slither();
        var snake = Owner!.Deck.Any(slither.CanEnchant)
            ? new EventOption(InitialOptionKey("SNAKE"), Snake)
            : new EventOption(InitialOptionKey("SNAKE_LOCKED"), null);
        return new[]
        {
            new EventOption(InitialOptionKey("BIRD"), Bird),
            snake,
            new EventOption(InitialOptionKey("TORUS"), Torus)
        };
    }

    private void Bird(RunState rs)
    {
        var owner = Owner!;
        var card = CardSelectCmd.FromDeckGeneric(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), new ChoiceSource(BirdSite, Id, ChoiceSource.NoInstance), c => c.IsTransformable(null) && c.Rarity == CardRarity.Basic).FirstOrDefault();
        if (card != null)
        {
            CardCmd.TransformTo(rs, card, PeckId);
        }
        SetEventFinished(Id + ".pages.BIRD.description");
    }

    private void Snake(RunState rs)
    {
        var owner = Owner!;
        var card = CardSelectCmd.FromDeckForEnchantment(rs, owner, new Slither(), 1, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), new ChoiceSource(SnakeSite, Id, ChoiceSource.NoInstance)).FirstOrDefault();
        if (card != null)
        {
            CardCmd.Enchant<Slither>(card, 1m);
        }
        SetEventFinished(Id + ".pages.SNAKE.description");
    }

    private void Torus(RunState rs)
    {
        var owner = Owner!;
        var card = CardSelectCmd.FromDeckGeneric(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), new ChoiceSource(TorusSite, Id, ChoiceSource.NoInstance), c => c != null && c.IsTransformable(null) && c.Rarity == CardRarity.Basic).FirstOrDefault();
        if (card != null)
        {
            CardCmd.TransformTo(rs, card, ToricToughnessId);
        }
        SetEventFinished(Id + ".pages.TORUS.description");
    }
}
