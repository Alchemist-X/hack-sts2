namespace Sts2Sim.Core.Cards;

public sealed class Nostalgia : CardDef
{
    public const string CardId = "NOSTALGIA";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;
    public const string NostalgiaPowerId = "NOSTALGIA_POWER";

    public Nostalgia()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var nostalgia = play.Card;
        var owner = nostalgia.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(NostalgiaPowerId), owner, PowerAmount, owner, nostalgia);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
