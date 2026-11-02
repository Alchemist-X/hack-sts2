namespace Sts2Sim.Core.Cards;

public sealed class MonarchsGaze : CardDef
{
    public const string CardId = "MONARCHS_GAZE";
    public const int Cost = 2;
    public const string MonarchsGazePowerId = "MONARCHS_GAZE_POWER";
    public const string StrengthLossKey = "StrengthLoss";
    public const decimal BaseStrengthLoss = 1m;
    public const int CostUpgrade = -1;

    public MonarchsGaze()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(StrengthLossKey, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(MonarchsGazePowerId), owner, card.Vars[StrengthLossKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
