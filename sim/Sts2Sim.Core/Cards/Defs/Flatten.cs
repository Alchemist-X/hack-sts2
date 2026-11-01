using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Flatten : CardDef
{
    public const string CardId = "FLATTEN";
    public const int Cost = 2;
    public const decimal BaseOstyDamage = 12m;
    public const decimal OstyDamageUpgrade = 4m;
    public const int ReducedCost = 0;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public Flatten()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new FlattenListener(card);

    public static bool HasOstyAttackedThisTurn(CombatState cs, CardInstance card)
    {
        var osty = PlayerPets.GetOsty(cs, card.Owner);
        var entries = cs.History.CreatureAttackedEntries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.IsByActor(osty) && e.HappenedThisTurn(cs))
            {
                return true;
            }
        }
        return false;
    }

    public static void ReduceCost(CardInstance card)
    {
        card.EnergyCost.SetThisTurn(ReducedCost);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var flatten = play.Card;
        var owner = flatten.Owner;
        if (Osty.CheckMissingWithAnim(cs, owner))
        {
            return;
        }
        DamageCmd.Attack(cs, flatten.Vars.OstyDamage.BaseValue).FromOsty(PlayerPets.GetOsty(cs, owner)!, flatten, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }

    public sealed class FlattenListener : CardListener
    {
        public FlattenListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new FlattenListener(newOwner);

        public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
        {
            if (!ReferenceEquals(card, Card))
            {
                return;
            }
            if (!HasOstyAttackedThisTurn(cs, Card))
            {
                return;
            }
            ReduceCost(Card);
        }

        public override void AfterAttack(CombatState cs, AttackCommand command)
        {
            if (command.Attacker == null)
            {
                return;
            }
            if (!ReferenceEquals(command.Attacker, PlayerPets.GetOsty(cs, Card.Owner)))
            {
                return;
            }
            ReduceCost(Card);
        }
    }
}
