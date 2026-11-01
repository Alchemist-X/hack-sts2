namespace Sts2Sim.Core.Cards;

public sealed class InfiniteBlades : CardDef
{
    public const string CardId = "INFINITE_BLADES";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;
    public const string InfiniteBladesPowerId = "INFINITE_BLADES_POWER";

    public InfiniteBlades()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var infiniteBlades = play.Card;
        var owner = infiniteBlades.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(InfiniteBladesPowerId), owner, PowerAmount, owner, infiniteBlades);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
