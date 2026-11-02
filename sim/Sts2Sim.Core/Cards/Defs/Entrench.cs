namespace Sts2Sim.Core.Cards;

public sealed class Entrench : CardDef
{
    public const string CardId = "ENTRENCH";
    public const int Cost = 2;
    public const int CostUpgrade = -1;

    public Entrench()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var owner = play.Card.Owner;
        CreatureCmd.GainBlock(cs, owner, owner.Block, ValueProp.Unpowered | ValueProp.Move, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
