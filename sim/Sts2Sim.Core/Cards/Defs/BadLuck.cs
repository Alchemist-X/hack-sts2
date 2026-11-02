using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BadLuck : CardDef
{
    public const string CardId = "BAD_LUCK";
    public const int Cost = -1;
    public const decimal BaseHpLoss = 13m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Eternal, CardKeyword.Unplayable });

    public BadLuck()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override bool CanBeGeneratedByModifiers => false;

    public override int MaxUpgradeLevel => 0;

    public override bool HasTurnEndInHandEffect => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss)
    };

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        CreatureCmd.Damage(cs, card.Owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, null);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
