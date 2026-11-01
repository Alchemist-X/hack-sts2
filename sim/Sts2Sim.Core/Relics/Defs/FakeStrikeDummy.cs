namespace Sts2Sim.Core.Relics;

public sealed class FakeStrikeDummy : RelicModel
{
    public const string RelicId = "FAKE_STRIKE_DUMMY";

    public const string ExtraDamageKey = "ExtraDamage";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ExtraDamageKey, 1m)
    };

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        if (cardSource == null)
        {
            return 0m;
        }
        if (!cardSource.HasTag(CardTag.Strike))
        {
            return 0m;
        }
        if (!ReferenceEquals(dealer, Owner) && !(cardSource.HasOwner && ReferenceEquals(cardSource.Owner, Owner)))
        {
            return 0m;
        }
        return DynamicVars[ExtraDamageKey].BaseValue;
    }
}
