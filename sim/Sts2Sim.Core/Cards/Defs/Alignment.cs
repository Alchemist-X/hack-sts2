namespace Sts2Sim.Core.Cards;

public sealed class Alignment : CardDef
{
    public const string CardId = "ALIGNMENT";
    public const int Cost = 0;
    public const int StarCost = 2;
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;

    public Alignment()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var alignment = play.Card;
        PlayerCmd.GainEnergy(cs, alignment.Vars.Energy.IntValue, alignment.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
