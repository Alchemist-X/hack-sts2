namespace Sts2Sim.Core.Cards;

public sealed class Genesis : CardDef
{
    public const string CardId = "GENESIS";
    public const int Cost = 2;
    public const string GenesisPowerId = "GENESIS_POWER";
    public const string StarsPerTurnKey = "StarsPerTurn";
    public const decimal BaseStarsPerTurn = 2m;
    public const decimal StarsPerTurnUpgrade = 1m;

    public Genesis()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(StarsPerTurnKey, BaseStarsPerTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var genesis = play.Card;
        var owner = genesis.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(GenesisPowerId), owner, genesis.Vars[StarsPerTurnKey].BaseValue, owner, genesis);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StarsPerTurnKey].UpgradeValueBy(StarsPerTurnUpgrade);
    }
}
