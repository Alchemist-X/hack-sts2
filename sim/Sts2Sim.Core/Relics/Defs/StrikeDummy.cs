namespace Sts2Sim.Core.Relics;

public sealed class StrikeDummy : RelicModel
{
    public const string RelicId = "STRIKE_DUMMY";

    public const string ExtraDamageKey = "ExtraDamage";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(ExtraDamageKey, 3m)
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
