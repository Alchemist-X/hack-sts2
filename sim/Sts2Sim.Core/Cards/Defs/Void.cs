using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Void : CardDef
{
    public const string CardId = "VOID";
    public const int Cost = -1;
    public const int BaseEnergy = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable, CardKeyword.Ethereal });

    public Void()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new VoidListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private sealed class VoidListener : CardListener
    {
        public VoidListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new VoidListener(newOwner);

        public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
        {
            if (!ReferenceEquals(card, Card))
            {
                return;
            }
            PlayerCmd.LoseEnergy(cs, Card.Vars.Energy.IntValue, Card.Owner);
        }
    }
}
