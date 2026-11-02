namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Glam : EnchantmentModel
{
    public const string EnchantmentId = "GLAM";
    public const string TimesKey = "Times";
    public const decimal BaseTimes = 1m;

    private bool _usedThisCombat;

    public override string Id => EnchantmentId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new(TimesKey, BaseTimes)
    };

    public bool UsedThisCombat
    {
        get => _usedThisCombat;
        private set => _usedThisCombat = value;
    }

    public override int EnchantPlayCount(int originalPlayCount)
    {
        if (UsedThisCombat)
        {
            return originalPlayCount;
        }
        return originalPlayCount + DynamicVars[TimesKey].IntValue;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (UsedThisCombat)
        {
            return;
        }
        if (!ReferenceEquals(cardPlay.Card, Card))
        {
            return;
        }
        UsedThisCombat = true;
        Status = EnchantmentStatus.Disabled;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_usedThisCombat);
    }
}
