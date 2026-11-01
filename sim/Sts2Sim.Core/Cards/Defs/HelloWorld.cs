namespace Sts2Sim.Core.Cards;

public sealed class HelloWorld : CardDef
{
    public const string CardId = "HELLO_WORLD";
    public const int Cost = 1;
    public const string PowerId = "HELLO_WORLD_POWER";
    public const decimal PowerAmount = 1m;

    public HelloWorld()
        : base(CardId, Cost, CardType.Power, CardRarity.Event, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var creature = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PowerId), creature, PowerAmount, creature, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
