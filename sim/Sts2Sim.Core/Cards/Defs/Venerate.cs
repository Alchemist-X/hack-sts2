namespace Sts2Sim.Core.Cards;

public sealed class Venerate : CardDef
{
    public const string CardId = "VENERATE";
    public const int Cost = 1;
    public const int BaseStars = 2;
    public const decimal StarsUpgrade = 1m;

    public Venerate()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var venerate = play.Card;
        PlayerCmd.GainStars(cs, venerate.Vars.Stars.BaseValue, venerate.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Stars.UpgradeValueBy(StarsUpgrade);
    }
}
