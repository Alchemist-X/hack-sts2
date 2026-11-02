namespace Sts2Sim.Core.Relics;

public sealed class NewLeaf : RelicModel
{
    public const string RelicId = "NEW_LEAF";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, DynamicVars.Cards.IntValue);
        var val2 = RelicDeckPromptsR91.FromDeckForTransformation(rs, Owner, prefs, RelicId).ToList();
        foreach (var current in val2)
        {
            CardCmd.TransformToRandom(rs, current, rs.Rng.Niche);
        }
    }
}
