namespace Sts2Sim.Core.Powers;

public sealed class EnragePower : PowerModel
{
    public const string PowerId = "ENRAGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Card.Type != CardType.Skill)
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
    }
}
