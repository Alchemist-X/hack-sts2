namespace Sts2Sim.Core.Powers;

public sealed class SpeedsterPower : PowerModel
{
    public const string PowerId = "SPEEDSTER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        var cardOwner = CombatStatus.CardOwnerCreature(cs, card);
        if (fromHandDraw || !ReferenceEquals(cardOwner, Owner) || cs.CurrentSide != cardOwner.Side)
        {
            return;
        }
        var hittableEnemies = new List<MonsterState>();
        cs.GetHittableEnemies(hittableEnemies);
        CreatureCmd.Damage(cs, hittableEnemies, Amount, ValueProp.Unpowered, Owner);
    }
}
