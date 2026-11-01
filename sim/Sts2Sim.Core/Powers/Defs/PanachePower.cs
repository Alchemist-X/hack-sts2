namespace Sts2Sim.Core.Powers;

public sealed class PanachePower : PowerModel
{
    public const string PowerId = "PANACHE_POWER";

    public const int BaseCardsLeft = 5;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public int CardsLeft { get; private set; } = BaseCardsLeft;

    public bool AlreadyApplied { get; private set; }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            return;
        }
        if (AlreadyApplied)
        {
            CardsLeft--;
            if (CardsLeft <= 0)
            {
                var hittable = new List<MonsterState>();
                cs.GetHittableEnemies(hittable);
                CreatureCmd.Damage(cs, hittable, Amount, ValueProp.Unpowered, Owner);
                CardsLeft = BaseCardsLeft;
            }
        }
        AlreadyApplied = true;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CardsLeft = BaseCardsLeft;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(CardsLeft);
        w.WriteBool(AlreadyApplied);
    }
}
