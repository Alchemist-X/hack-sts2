namespace Sts2Sim.Core.Relics;

public sealed class ChemicalX : RelicModel
{
    public const string RelicId = "CHEMICAL_X";

    public const string IncreaseKey = "Increase";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(IncreaseKey, 2m)
    };

    public override int ModifyXValue(CombatState cs, CardInstance card, int originalValue)
    {
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return originalValue;
        }
        return originalValue + DynamicVars[IncreaseKey].IntValue;
    }
}
