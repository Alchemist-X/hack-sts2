namespace Sts2Sim.Core.Powers;

public sealed class DanseMacabrePower : PowerModel
{
    public const string PowerId = "DANSE_MACABRE_POWER";

    public const int Energy = 2;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) && card.EnergyCost.GetResolved(cs, card) >= Energy)
        {
            CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        }
    }
}
