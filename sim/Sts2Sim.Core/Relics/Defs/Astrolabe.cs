namespace Sts2Sim.Core.Relics;

public sealed class Astrolabe : RelicModel
{
    public const string RelicId = "ASTROLABE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, DynamicVars.Cards.IntValue);
        var val2 = RelicDeckPromptsR91.FromDeckForTransformation(rs, Owner, prefs, RelicId).ToList();
        foreach (var current in val2)
        {
            var cardModel = CardFactory.CreateRandomCardForTransform(rs, current, false, rs.Rng.Niche);
            CardCmd.Upgrade(null, cardModel);
            CardCmd.Transform(rs, current, cardModel);
        }
    }
}
