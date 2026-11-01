namespace Sts2Sim.Core.Relics;

public sealed class PreservedFog : RelicModel
{
    public const string RelicId = "PRESERVED_FOG";

    public const string FollyCardId = "FOLLY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, DynamicVars.Cards.IntValue);
        var enumerable = RelicDeckPromptsR91.FromDeckForRemoval(rs, Owner, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardPileCmd.RemoveFromDeck(rs, current);
        }
        CardPileCmd.AddCurseToDeck(rs, FollyCardId, Owner);
    }
}
