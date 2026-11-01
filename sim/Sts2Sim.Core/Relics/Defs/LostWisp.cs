namespace Sts2Sim.Core.Relics;

public sealed class LostWisp : RelicModel
{
    public const string RelicId = "LOST_WISP";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Unpowered)
    };

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.HasOwner && ReferenceEquals(card.Owner, Owner) && cs.IsInProgress && card.Type == CardType.Power)
        {
            var damage = DynamicVars.Damage;
            CreatureCmd.Damage(cs, HittableEnemies(cs), damage.BaseValue, damage.Props, Owner);
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
