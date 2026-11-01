namespace Sts2Sim.Core.Powers;

public sealed class HauntPower : PowerModel
{
    public const string PowerId = "HAUNT_POWER";

    public const string SoulCardId = "SOUL";

    public const ValueProp HauntDamageProps = ValueProp.Unblockable | ValueProp.Unpowered;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!string.Equals(card.Id, SoulCardId, StringComparison.Ordinal) || !ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        var hittableEnemies = HittableEnemies(cs);
        if (hittableEnemies.Count != 0)
        {
            var item = cs.Rng.CombatTargets.NextItem(hittableEnemies)!;
            CreatureCmd.Damage(cs, new[] { item }, Amount, HauntDamageProps, null, null, null);
        }
    }

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }
}
