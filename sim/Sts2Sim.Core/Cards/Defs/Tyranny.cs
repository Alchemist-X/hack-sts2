namespace Sts2Sim.Core.Cards;

public sealed class Tyranny : CardDef
{
    public const string CardId = "TYRANNY";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;
    public const string TyrannyPowerId = "TYRANNY_POWER";

    public Tyranny()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(TyrannyPowerId), owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
