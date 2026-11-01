namespace Sts2Sim.Core.Cards;

public sealed class PhotonCut : CardDef
{
    public const string CardId = "PHOTON_CUT";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const int BaseCards = 1;
    public const string PutBackKey = "PutBack";
    public const decimal BasePutBack = 1m;
    public const decimal DamageUpgrade = 3m;
    public const decimal CardsUpgrade = 1m;

    public PhotonCut()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards),
        new DynamicVar(PutBackKey, BasePutBack)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, card.Vars[PutBackKey].IntValue);
        var cards = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        CardPileCmd.Add(cs, cards, PileType.Draw, CardPilePosition.Top);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
