namespace Sts2Sim.Core.Cards;

public sealed class Headbutt : CardDef
{
    public const string CardId = "HEADBUTT";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal DamageUpgrade = 3m;
    public const int SelectCount = 1;

    public Headbutt()
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
        var headbutt = play.Card;
        DamageCmd.Attack(cs, headbutt.Vars.Damage.BaseValue).FromCard(headbutt, play).Targeting(target).Execute();
        var owner = headbutt.Owner;
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromCombatPile(cs, owner.DiscardPile, owner, prefs, ChoiceSource.For(headbutt, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Draw, CardPilePosition.Top);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
