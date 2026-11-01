namespace Sts2Sim.Core.Cards;

public sealed class CreativeAi : CardDef
{
    public const string CardId = "CREATIVE_AI";
    public const int Cost = 3;
    public const int CostUpgrade = -1;
    public const string CreativeAiPowerId = "CREATIVE_AI_POWER";
    public const string CreativeAiKey = "CreativeAi";
    public const decimal BaseCreativeAi = 1m;

    public CreativeAi()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CreativeAiKey, BaseCreativeAi)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var creativeAi = play.Card;
        var owner = creativeAi.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(CreativeAiPowerId), owner, creativeAi.Vars[CreativeAiKey].BaseValue, owner, creativeAi);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
