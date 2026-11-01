namespace Sts2Sim.Core.Powers;

public sealed class MasterPlannerPower : PowerModel
{
    public const string PowerId = "MASTER_PLANNER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState))
        {
            return;
        }
        if (cardPlay.Card.Type != CardType.Skill)
        {
            return;
        }
        CardCmd.ApplyKeyword(cardPlay.Card, CardKeyword.Sly);
    }
}
