namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class TezcatarasEmber : EnchantmentModel
{
    public const string EnchantmentId = "TEZCATARAS_EMBER";
    public const decimal BaseDamage = 3m;

    public override string Id => EnchantmentId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    protected override void OnEnchant()
    {
        var card = Card!;
        card.EnergyCost.UpgradeBy(-card.EnergyCost.GetWithModifiers(CostModifiers.None));
        card.AddKeyword(CardKeyword.Eternal);
    }

    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return DynamicVars.Damage.BaseValue;
    }
}
