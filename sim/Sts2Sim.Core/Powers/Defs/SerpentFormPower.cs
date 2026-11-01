namespace Sts2Sim.Core.Powers;

public sealed class SerpentFormPower : PowerModel
{
    public const string PowerId = "SERPENT_FORM_POWER";

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, Amount);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner) || !_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int damage) || damage <= 0)
        {
            return;
        }
        var hittableEnemies = new List<MonsterState>();
        cs.GetHittableEnemies(hittableEnemies);
        var creature = cs.Rng.CombatTargets.NextItem(hittableEnemies);
        if (creature != null)
        {
            CreatureCmd.Damage(cs, creature, damage, ValueProp.Unpowered, Owner);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((SerpentFormPower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        WriteCardAmounts(w, _amountsForPlayedCards);
    }

    private static void WriteCardAmounts(CanonicalWriter w, Dictionary<int, int> amounts)
    {
        var keys = new List<int>(amounts.Keys);
        keys.Sort();
        w.WriteInt32(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            w.WriteInt32(keys[i]);
            w.WriteInt32(amounts[keys[i]]);
        }
    }
}
