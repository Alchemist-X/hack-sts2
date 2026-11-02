namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Steady : EnchantmentModel
{
    public const string EnchantmentId = "STEADY";

    public override string Id => EnchantmentId;

    protected override void OnEnchant()
    {
        Card!.AddKeyword(CardKeyword.Retain);
    }
}
