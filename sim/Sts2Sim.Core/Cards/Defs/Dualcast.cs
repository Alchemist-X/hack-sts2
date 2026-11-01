namespace Sts2Sim.Core.Cards;

public sealed class Dualcast : CardDef
{
    public const string CardId = "DUALCAST";
    public const int Cost = 1;
    public const int CostUpgrade = -1;

    public Dualcast()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var dualcast = play.Card;
        var owner = dualcast.Owner;
        if (owner.OrbQueue.Orbs.Count > 0)
        {
            OrbCmd.EvokeNext(cs, owner, dequeue: false);
            OrbCmd.EvokeNext(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
