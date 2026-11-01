namespace Sts2Sim.Core.Cards;

public sealed class CallOfTheVoid : CardDef
{
    public const string CardId = "CALL_OF_THE_VOID";
    public const int Cost = 1;
    public const string PowerId = "CALL_OF_THE_VOID_POWER";
    public const int BaseCards = 1;

    public CallOfTheVoid()
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
        var creature = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PowerId), creature, card.Vars.Cards.BaseValue, creature, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
