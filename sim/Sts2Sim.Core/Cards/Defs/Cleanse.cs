namespace Sts2Sim.Core.Cards;

public sealed class Cleanse : CardDef
{
    public const string CardId = "CLEANSE";
    public const int Cost = 1;
    public const decimal BaseSummon = 3m;
    public const decimal SummonUpgrade = 2m;
    public const int SelectCount = 1;

    public Cleanse()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var cleanse = play.Card;
        var owner = cleanse.Owner;
        OstyCmd.Summon(cs, owner, cleanse.Vars.Summon.BaseValue, null);
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, SelectCount);
        var cardModel = CardSelectCmd.FromCombatPile(cs, owner.DrawPile, owner, prefs, ChoiceSource.For(cleanse, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
