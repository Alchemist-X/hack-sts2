namespace Sts2Sim.Core.Cards;

public sealed class ForegoneConclusion : CardDef
{
    public const string CardId = "FOREGONE_CONCLUSION";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;
    public const string ForegoneConclusionPowerId = "FOREGONE_CONCLUSION_POWER";

    public ForegoneConclusion()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ForegoneConclusionPowerId), owner, card.Vars.Cards.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
