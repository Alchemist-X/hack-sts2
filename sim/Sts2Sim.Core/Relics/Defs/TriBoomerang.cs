namespace Sts2Sim.Core.Relics;

public sealed class TriBoomerang : RelicModel
{
    public const string RelicId = "TRI_BOOMERANG";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, DynamicVars.Cards.IntValue);
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, new Instinct(), prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant<Instinct>(current, 1m);
        }
    }
}
