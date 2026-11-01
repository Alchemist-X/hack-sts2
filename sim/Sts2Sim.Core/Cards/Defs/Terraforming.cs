namespace Sts2Sim.Core.Cards;

public sealed class Terraforming : CardDef
{
    public const string CardId = "TERRAFORMING";
    public const int Cost = 1;
    public const string PowerVarName = "VigorPower";
    public const decimal BaseVigor = 7m;
    public const decimal VigorUpgrade = 3m;
    public const string VigorPowerId = "VIGOR_POWER";

    public Terraforming()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseVigor)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, VigorPowerId, owner, card.Vars[PowerVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(VigorUpgrade);
    }
}
