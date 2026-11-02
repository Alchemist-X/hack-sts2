namespace Sts2Sim.Core.Relics;

public sealed class PrecariousShears : RelicModel
{
    public const string RelicId = "PRECARIOUS_SHEARS";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2),
        new DamageVar(16m, ValueProp.Unpowered)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, DynamicVars.Cards.IntValue);
        var enumerable = RelicDeckPromptsR91.FromDeckForRemoval(rs, Owner, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardPileCmd.RemoveFromDeck(rs, current);
        }
        if (rs.Combat != null)
        {
            CreatureCmd.Damage(rs.Combat, Owner, DynamicVars.Damage, null, null, null);
            return;
        }
        CreatureCmd.Damage(rs, Owner, DynamicVars.Damage.BaseValue, DynamicVars.Damage.Props);
    }
}
