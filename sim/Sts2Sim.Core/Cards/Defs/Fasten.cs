namespace Sts2Sim.Core.Cards;

public sealed class Fasten : CardDef
{
    public const string CardId = "FASTEN";
    public const int Cost = 1;
    public const string FastenPowerId = "FASTEN_POWER";
    public const string ExtraBlockKey = "ExtraBlock";
    public const decimal BaseExtraBlock = 4m;
    public const decimal ExtraBlockUpgrade = 2m;

    public Fasten()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ExtraBlockKey, BaseExtraBlock)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FastenPowerId), owner, card.Vars[ExtraBlockKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ExtraBlockKey].UpgradeValueBy(ExtraBlockUpgrade);
    }
}
