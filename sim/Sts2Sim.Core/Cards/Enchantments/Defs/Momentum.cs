namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Momentum : EnchantmentModel
{
    public const string EnchantmentId = "MOMENTUM";

    private int _extraDamage;

    public override string Id => EnchantmentId;

    public int ExtraDamage
    {
        get => _extraDamage;
        private set => _extraDamage = value;
    }

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Attack;

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        ExtraDamage += Amount;
    }

    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return ExtraDamage;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_extraDamage);
    }
}
