namespace Sts2Sim.Core.Cards;

public sealed class ShadowStep : CardDef
{
    public const string CardId = "SHADOW_STEP";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const int BaseCards = 3;
    public const decimal ShadowStepAmount = 1m;
    public const string ShadowStepPowerId = "SHADOW_STEP_POWER";

    public ShadowStep()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var shadowStep = play.Card;
        var owner = shadowStep.Owner;
        CardCmd.Discard(cs, owner.Hand.Cards.ToList());
        PowerCmd.Apply(cs, PowerRegistry.Create(ShadowStepPowerId), owner, ShadowStepAmount, owner, shadowStep);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
