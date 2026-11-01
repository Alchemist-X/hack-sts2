namespace Sts2Sim.Core.Cards;

public sealed class MachineLearning : CardDef
{
    public const string CardId = "MACHINE_LEARNING";
    public const int Cost = 1;
    public const string MachineLearningPowerId = "MACHINE_LEARNING_POWER";
    public const int BaseCards = 1;

    public MachineLearning()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
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
        PowerCmd.Apply(cs, PowerRegistry.Create(MachineLearningPowerId), owner, card.Vars.Cards.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
