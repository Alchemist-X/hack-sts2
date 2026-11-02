using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class ByrdonisEgg : CardDef
{
    public const string CardId = "BYRDONIS_EGG";
    public const int Cost = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public ByrdonisEgg()
        : base(CardId, Cost, CardType.Quest, CardRarity.Quest, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override AbstractModel? CreateListener(CardInstance card) => new ByrdonisEggListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private sealed class ByrdonisEggListener : CardListener
    {
        public ByrdonisEggListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new ByrdonisEggListener(newOwner);

        public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
        {
            if (!ReferenceEquals(player, Card.Owner))
            {
                return false;
            }
            options.Add(new HatchRestSiteOption(player));
            return true;
        }
    }
}
