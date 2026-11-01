namespace Sts2Sim.Core.Relics;

public sealed class RingOfTheDrake : RelicModel
{
    public const string RelicId = "RING_OF_THE_DRAKE";

    public const string TurnsKey = "Turns";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2),
        new DynamicVar(TurnsKey, 3m)
    };

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        if (Owner.TurnNumber > DynamicVars[TurnsKey].BaseValue)
        {
            return count;
        }
        return count + DynamicVars.Cards.BaseValue;
    }
}
