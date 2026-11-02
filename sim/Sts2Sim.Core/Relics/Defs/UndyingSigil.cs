namespace Sts2Sim.Core.Relics;

public sealed class UndyingSigil : RelicModel
{
    public const string RelicId = "UNDYING_SIGIL";

    public const string DoomPowerId = "DOOM_POWER";

    public const string DamageDecreaseKey = "DamageDecrease";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DamageDecreaseKey, 0.5m)
    };

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (dealer == null)
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (ReferenceEquals(dealer, Owner))
        {
            return 1m;
        }
        if (dealer.CurrentHp > (dealer.GetPower(DoomPowerId)?.Amount ?? 0))
        {
            return 1m;
        }
        return DynamicVars[DamageDecreaseKey].BaseValue;
    }
}
