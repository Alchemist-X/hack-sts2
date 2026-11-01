namespace Sts2Sim.Core.Cards;

public sealed class TheSmith : CardDef
{
    public const string CardId = "THE_SMITH";
    public const int Cost = 1;
    public const int StarCost = 4;
    public const int BaseForge = 30;
    public const decimal ForgeUpgrade = 10m;

    public TheSmith()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var theSmith = play.Card;
        ForgeCmd.Forge(cs, theSmith.Vars.Forge.IntValue, theSmith.Owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
