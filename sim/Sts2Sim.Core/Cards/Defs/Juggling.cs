namespace Sts2Sim.Core.Cards;

public sealed class Juggling : CardDef
{
    public const string CardId = "JUGGLING";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;
    public const string JugglingPowerId = "JUGGLING_POWER";

    public Juggling()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var juggling = play.Card;
        var owner = juggling.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(JugglingPowerId), owner, PowerAmount, owner, juggling);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
