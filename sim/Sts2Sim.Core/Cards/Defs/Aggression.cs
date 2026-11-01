namespace Sts2Sim.Core.Cards;

public sealed class Aggression : CardDef
{
    public const string CardId = "AGGRESSION";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;

    public Aggression()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var aggression = play.Card;
        var owner = aggression.Owner;
        PowerCmd.Apply<AggressionPower>(cs, owner, PowerAmount, owner, aggression);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
