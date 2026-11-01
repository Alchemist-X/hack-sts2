namespace Sts2Sim.Core.Cards;

public sealed class Unmovable : CardDef
{
    public const string CardId = "UNMOVABLE";
    public const int Cost = 2;
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;
    public const string UnmovablePowerId = "UNMOVABLE_POWER";

    public Unmovable()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var unmovable = play.Card;
        var owner = unmovable.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(UnmovablePowerId), owner, PowerAmount, owner, unmovable);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
