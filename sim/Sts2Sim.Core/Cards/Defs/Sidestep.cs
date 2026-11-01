namespace Sts2Sim.Core.Cards;

public sealed class Sidestep : CardDef
{
    public const string CardId = "SIDESTEP";
    public const int Cost = 0;
    public const int BaseEnergy = 1;
    public const decimal EnergyUpgrade = 1m;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";

    public Sidestep()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, EnergyNextTurnPowerId, owner, card.Vars.Energy.IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
