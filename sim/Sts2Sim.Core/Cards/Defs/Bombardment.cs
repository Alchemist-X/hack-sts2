using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Bombardment : CardDef
{
    public const string CardId = "BOMBARDMENT";
    public const int Cost = 3;
    public const decimal BaseDamage = 18m;
    public const decimal DamageUpgrade = 6m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Bombardment()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new BombardmentListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var bombardment = play.Card;
        DamageCmd.Attack(cs, bombardment.Vars.Damage.BaseValue).FromCard(bombardment, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private sealed class BombardmentListener : CardListener
    {
        public BombardmentListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new BombardmentListener(newOwner);

        public override void AfterAutoPrePlayPhaseEnteredEarly(CombatState cs, PlayerState player)
        {
            var bombardment = Card;
            var pile = bombardment.GetCombatPile(cs);
            if (pile != null && pile.Type == PileType.Exhaust && ReferenceEquals(player, bombardment.Owner))
            {
                CardCmd.AutoPlay(cs, bombardment, null);
            }
        }
    }
}
