namespace Sts2Sim.Core.Cards;

public sealed class Subroutine : CardDef
{
    public const string CardId = "SUBROUTINE";
    public const int Cost = 1;
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;
    public const string SubroutinePowerId = "SUBROUTINE_POWER";

    public Subroutine()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, SubroutinePowerId, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
