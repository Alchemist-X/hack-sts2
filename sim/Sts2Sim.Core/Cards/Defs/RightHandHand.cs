using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class RightHandHand : CardDef
{
    public const string CardId = "RIGHT_HAND_HAND";
    public const int Cost = 0;
    public const decimal BaseOstyDamage = 4m;
    public const int BaseEnergy = 2;
    public const decimal OstyDamageUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public RightHandHand()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new RightHandHandListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var rightHandHand = play.Card;
        var owner = rightHandHand.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            DamageCmd.Attack(cs, rightHandHand.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, rightHandHand, play).Targeting(target).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }

    private sealed class RightHandHandListener : CardListener
    {
        public RightHandHandListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new RightHandHandListener(newOwner);

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay)
        {
            var rightHandHand = Card;
            if (ReferenceEquals(CardPileCmd.OwnerOf(cs, cardPlay.Card), rightHandHand.Owner) && cardPlay.Resources.EnergyValue >= rightHandHand.Vars.Energy.IntValue)
            {
                var pile = rightHandHand.GetCombatPile(cs);
                if (pile != null && pile.Type == PileType.Discard)
                {
                    CardPileCmd.Add(cs, rightHandHand, PileType.Hand);
                }
            }
        }
    }
}
