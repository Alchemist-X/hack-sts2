namespace Sts2Sim.Core.Relics;

public sealed class BiiigHug : RelicModel
{
    public const string RelicId = "BIIIG_HUG";

    public const string SootCardId = "SOOT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(4)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, DynamicVars.Cards.IntValue);
        var cards = RelicDeckPromptsR91.FromDeckForRemoval(rs, Owner, prefs, RelicId).ToList();
        CardPileCmd.RemoveFromDeck(rs, cards);
    }

    public override void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
        if (ReferenceEquals(shuffler, Owner))
        {
            var soot = cs.CreateCard(SootCardId, Owner);
            CardPileCmd.AddGeneratedCardToCombat(cs, soot, PileType.Draw, Owner, CardPilePosition.Random);
        }
    }
}
