namespace Sts2Sim.Core.Cards;

public sealed class HiddenCache : CardDef
{
    public const string CardId = "HIDDEN_CACHE";
    public const int Cost = 1;
    public const string StarNextTurnPowerId = "STAR_NEXT_TURN_POWER";
    public const string StarNextTurnPowerKey = "StarNextTurnPower";
    public const int BaseStars = 1;
    public const decimal BaseStarNextTurn = 3m;
    public const decimal StarNextTurnUpgrade = 1m;

    public HiddenCache()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StarsVar(BaseStars),
        new PowerVar<PowerModel>(StarNextTurnPowerKey, BaseStarNextTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hiddenCache = play.Card;
        var owner = hiddenCache.Owner;
        PlayerCmd.GainStars(cs, hiddenCache.Vars.Stars.BaseValue, owner);
        PowerCmd.Apply(cs, PowerRegistry.Create(StarNextTurnPowerId), owner, hiddenCache.Vars[StarNextTurnPowerKey].BaseValue, owner, hiddenCache);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StarNextTurnPowerKey].UpgradeValueBy(StarNextTurnUpgrade);
    }
}
