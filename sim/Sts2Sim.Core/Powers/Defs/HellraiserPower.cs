namespace Sts2Sim.Core.Powers;

public sealed class HellraiserPower : PowerModel
{
    public const string PowerId = "HELLRAISER_POWER";

    public const int InfiniteAutoPlayCap = 9;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public int InfiniteAutoPlaysThisTurn { get; private set; }

    public override void AfterCardDrawnEarly(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || !card.HasTag(CardTag.Strike))
        {
            return;
        }
        bool autoPlay = true;
        if (AllHittableEnemiesShowInfiniteHp(cs))
        {
            if (InfiniteAutoPlaysThisTurn >= InfiniteAutoPlayCap)
            {
                autoPlay = false;
            }
            InfiniteAutoPlaysThisTurn++;
        }
        else
        {
            ResetInfiniteAutoPlayData();
        }
        if (autoPlay)
        {
            CardCmdForwarders.AutoPlay(cs, card, null);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        ResetInfiniteAutoPlayData();
    }

    private void ResetInfiniteAutoPlayData()
    {
        InfiniteAutoPlaysThisTurn = 0;
    }

    private static bool AllHittableEnemiesShowInfiniteHp(CombatState cs)
    {
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            var monster = monsters[i];
            if (CombatStatus.IsHittable(cs, monster) && !ShowsInfiniteHp(monster))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ShowsInfiniteHp(CreatureState creature) => false;

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(InfiniteAutoPlaysThisTurn);
    }
}
