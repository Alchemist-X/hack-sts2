namespace Sts2Sim.Core.Relics;

public sealed class Pomander : RelicModel
{
    public const string RelicId = "POMANDER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, DynamicVars.Cards.IntValue);
        var val2 = RelicDeckPromptsR91.FromDeckForUpgrade(rs, Owner, prefs, RelicId).ToList();
        foreach (var current in val2)
        {
            CardCmd.Upgrade(null, current);
        }
    }
}
