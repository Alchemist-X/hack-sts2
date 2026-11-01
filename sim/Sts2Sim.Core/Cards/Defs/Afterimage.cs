namespace Sts2Sim.Core.Cards;

public sealed class Afterimage : CardDef
{
    public const string CardId = "AFTERIMAGE";
    public const int Cost = 1;
    public const string AfterimagePowerId = "AFTERIMAGE_POWER";
    public const string PowerVarName = "AfterimagePower";
    public const decimal BaseAfterimage = 1m;

    public Afterimage()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseAfterimage)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var afterimage = play.Card;
        var owner = afterimage.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(AfterimagePowerId), owner, afterimage.Vars[PowerVarName].BaseValue, owner, afterimage);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
