using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class NeowsFury : CardDef
{
    public const string CardId = "NEOWS_FURY";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const int BaseCards = 2;
    public const decimal DamageUpgrade = 4m;
    public const decimal CardsUpgrade = 1m;
    public const int MinSelect = 0;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public NeowsFury()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool CanBeGeneratedInCombat => false;

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
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        int count = Math.Min(card.Vars.Cards.IntValue, CardPile.MaxCardsInHand - owner.Hand.Cards.Count);
        if (count > 0)
        {
            var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, MinSelect, count);
            var cards = CardSelectCmd.FromCombatPile(cs, owner.DiscardPile, owner, prefs, ChoiceSource.For(card, "OnPlay")).ToList();
            CardPileCmd.Add(cs, cards, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
