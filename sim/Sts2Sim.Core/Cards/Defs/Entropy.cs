namespace Sts2Sim.Core.Cards;

public sealed class Entropy : CardDef
{
    public const string CardId = "ENTROPY";
    public const int Cost = 1;
    public const int BaseCards = 1;

    public Entropy()
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
        PowerCmd.Apply<EntropyPower>(cs, owner, card.Vars.Cards.IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
