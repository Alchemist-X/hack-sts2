namespace Sts2Sim.Core.Cards;

public sealed class DaggerThrow : CardDef
{
    public const string CardId = "DAGGER_THROW";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal DamageUpgrade = 3m;
    public const decimal DrawCount = 1m;
    public const int DiscardCount = 1;

    public DaggerThrow()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

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
        CardPileCmd.Draw(cs, DrawCount, owner);
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, DiscardCount);
        var cardModel = CardSelectCmd.FromHandForDiscard(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Discard(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
