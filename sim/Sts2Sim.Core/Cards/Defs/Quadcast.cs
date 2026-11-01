namespace Sts2Sim.Core.Cards;

public sealed class Quadcast : CardDef
{
    public const string CardId = "QUADCAST";
    public const int Cost = 1;
    public const int BaseRepeat = 4;
    public const int CostUpgrade = -1;

    public Quadcast()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var quadcast = play.Card;
        var owner = quadcast.Owner;
        if (owner.OrbQueue.Orbs.Count <= 0)
        {
            return;
        }
        for (int i = 0; i < quadcast.Vars.Repeat.IntValue; i++)
        {
            OrbCmd.EvokeNext(cs, owner, i == quadcast.Vars.Repeat.IntValue - 1);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
