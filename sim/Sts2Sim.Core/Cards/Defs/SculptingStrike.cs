using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SculptingStrike : CardDef
{
    public const string CardId = "SCULPTING_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal DamageUpgrade = 3m;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public SculptingStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, c => !c.GetKeywordsWithSources(cs, KeywordSources.Local).Contains(CardKeyword.Ethereal), ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.ApplyKeyword(cardModel, CardKeyword.Ethereal);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
