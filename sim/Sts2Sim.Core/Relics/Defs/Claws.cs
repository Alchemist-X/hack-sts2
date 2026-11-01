namespace Sts2Sim.Core.Relics;

[SimExclude]
public sealed class Claws : RelicModel
{
    public const string RelicId = "CLAWS";

    public const string MaulCardId = "MAUL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(6)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(RelicId + ".selectionScreenPrompt", 0, DynamicVars.Cards.IntValue)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };
        var enumerable = RelicDeckPromptsR91.FromDeckForTransformation(rs, Owner, prefs, RelicId);
        var transformations = enumerable.Select(original => (Original: original, Replacement: CreateMaulFromOriginal(rs, original))).ToList();
        if (transformations.Count > 0)
        {
            throw new NotImplementedException("W3 gap: CardCmd.Transform of deck cards outside a combat (Commands/CardCmd.cs:2052-2065)");
        }
    }

    private CardInstance CreateMaulFromOriginal(RunState rs, CardInstance original)
    {
        var cardModel = CardFactory.CreateRunCard(rs, CardRegistry.Get(MaulCardId), Owner);
        if (original.IsUpgraded && cardModel.IsUpgradable)
        {
            CardCmd.Upgrade(null, cardModel);
        }
        if (original.Enchantment != null)
        {
            var enchantmentModel = original.Enchantment.MutableClone();
            if (enchantmentModel.CanEnchant(cardModel))
            {
                CardCmd.Enchant(enchantmentModel, cardModel, enchantmentModel.Amount);
            }
        }
        return cardModel;
    }
}
