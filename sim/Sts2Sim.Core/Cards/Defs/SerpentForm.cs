namespace Sts2Sim.Core.Cards;

public sealed class SerpentForm : CardDef
{
    public const string CardId = "SERPENT_FORM";
    public const int Cost = 3;
    public const string PowerVarName = "SerpentFormPower";
    public const decimal BasePower = 4m;
    public const decimal PowerUpgrade = 2m;
    public const string SerpentFormPowerId = "SERPENT_FORM_POWER";

    public SerpentForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, SerpentFormPowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(PowerUpgrade);
    }
}
