namespace Sts2Sim.Core.Cards;

public sealed class Brand : CardDef
{
    public const string CardId = "BRAND";
    public const int Cost = 0;
    public const decimal BaseHpLoss = 1m;
    public const decimal BaseStrength = 1m;
    public const decimal StrengthUpgrade = 1m;
    public const int SelectCount = 1;

    public Brand()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss),
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.Damage(cs, owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.ExhaustSelectionPrompt, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Exhaust(cs, cardModel);
        }
        PowerCmd.Apply<StrengthPower>(cs, owner, card.Vars.Strength.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
