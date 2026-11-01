namespace Sts2Sim.Core.Cards;

public sealed class HeirloomHammer : CardDef
{
    public const string CardId = "HEIRLOOM_HAMMER";
    public const int Cost = 2;
    public const decimal BaseDamage = 20m;
    public const decimal DamageUpgrade = 5m;
    public const int BaseRepeat = 1;
    public const int SelectCount = 1;

    public HeirloomHammer()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var selection = CardSelectCmd.FromHand(cs, owner, prefs, static c => c.Def.VisualCardPool.IsColorless, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (selection == null)
        {
            return;
        }
        for (int i = 0; i < card.Vars.Repeat.IntValue; i++)
        {
            var clone = cs.CloneCard(selection);
            clone.ExhaustOnNextPlay = false;
            CardPileCmd.AddGeneratedCardToCombat(cs, clone, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
