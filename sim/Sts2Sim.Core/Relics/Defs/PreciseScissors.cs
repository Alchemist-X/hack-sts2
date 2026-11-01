namespace Sts2Sim.Core.Relics;

public sealed class PreciseScissors : RelicModel
{
    public const string RelicId = "PRECISE_SCISSORS";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, DynamicVars.Cards.IntValue);
        var enumerable = RelicDeckPromptsR91.FromDeckForRemoval(rs, Owner, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardPileCmd.RemoveFromDeck(rs, current);
        }
    }
}
