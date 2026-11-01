namespace Sts2Sim.Core.Cards;

public sealed class Loop : CardDef
{
    public const string CardId = "LOOP";
    public const int Cost = 1;
    public const string LoopPowerId = "LOOP_POWER";
    public const string LoopVar = "Loop";
    public const decimal BaseLoop = 1m;
    public const decimal LoopUpgrade = 1m;

    public Loop()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(LoopVar, BaseLoop)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var loop = play.Card;
        var owner = loop.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(LoopPowerId), owner, loop.Vars[LoopVar].BaseValue, owner, loop);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[LoopVar].UpgradeValueBy(LoopUpgrade);
    }
}
