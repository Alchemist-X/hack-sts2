namespace Sts2Sim.Core.Cards;

public sealed class Convergence : CardDef
{
    public const string CardId = "CONVERGENCE";
    public const int Cost = 1;
    public const string RetainHandPowerId = "RETAIN_HAND_POWER";
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const string StarNextTurnPowerId = "STAR_NEXT_TURN_POWER";
    public const decimal RetainHandAmount = 1m;
    public const int BaseEnergy = 1;
    public const int BaseStars = 1;
    public const decimal StarsUpgrade = 1m;

    public Convergence()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy),
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var convergence = play.Card;
        var owner = convergence.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(RetainHandPowerId), owner, RetainHandAmount, owner, convergence);
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, convergence.Vars.Energy.BaseValue, owner, convergence);
        PowerCmd.Apply(cs, PowerRegistry.Create(StarNextTurnPowerId), owner, convergence.Vars.Stars.BaseValue, owner, convergence);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Stars.UpgradeValueBy(StarsUpgrade);
    }
}
