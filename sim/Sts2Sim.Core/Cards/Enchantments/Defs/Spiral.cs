namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Spiral : EnchantmentModel
{
    public const string EnchantmentId = "SPIRAL";
    public const string TimesKey = "Times";
    public const decimal BaseTimes = 1m;

    public override string Id => EnchantmentId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new IntVar(TimesKey, BaseTimes)
    };

    public override bool CanEnchant(CardInstance c)
    {
        if (base.CanEnchant(c) && c.Rarity == CardRarity.Basic)
        {
            if (!c.HasTag(CardTag.Strike))
            {
                return c.HasTag(CardTag.Defend);
            }
            return true;
        }
        return false;
    }

    public override int EnchantPlayCount(int originalPlayCount) => originalPlayCount + DynamicVars[TimesKey].IntValue;
}
