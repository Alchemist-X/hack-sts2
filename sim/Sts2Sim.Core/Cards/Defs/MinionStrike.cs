using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MinionStrike : CardDef
{
    public const string CardId = "MINION_STRIKE";
    public const int Cost = 0;
    public const decimal BaseDamage = 6m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });
    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike, CardTag.Minion });

    public MinionStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
