using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Enthralled : CardDef
{
    public const string CardId = "ENTHRALLED";
    public const int Cost = 2;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Eternal });

    public Enthralled()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override bool CanBeGeneratedByModifiers => false;

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override AbstractModel? CreateListener(CardInstance card) => new EnthralledListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private sealed class EnthralledListener : CardListener
    {
        public EnthralledListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new EnthralledListener(newOwner);

        public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
        {
            var enthralled = Card;
            if (!ReferenceEquals(card.Owner, enthralled.Owner))
            {
                return true;
            }
            var pile = enthralled.GetCombatPile(cs);
            if (pile == null || pile.Type != PileType.Hand)
            {
                return true;
            }
            if (card.Def is Enthralled)
            {
                return true;
            }
            if (autoPlayType != AutoPlayType.None)
            {
                return true;
            }
            return false;
        }
    }
}
