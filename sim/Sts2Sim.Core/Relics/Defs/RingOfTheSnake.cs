namespace Sts2Sim.Core.Relics;

public sealed class RingOfTheSnake : RelicModel
{
    public const string RelicId = "RING_OF_THE_SNAKE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
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
