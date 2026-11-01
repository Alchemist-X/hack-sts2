namespace Sts2Sim.Core.Relics;

public sealed class BagOfPreparation : RelicModel
{
    public const string RelicId = "BAG_OF_PREPARATION";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (Owner.TurnNumber > 1)
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }
}
