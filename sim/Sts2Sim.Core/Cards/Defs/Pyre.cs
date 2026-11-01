namespace Sts2Sim.Core.Cards;

public sealed class Pyre : CardDef
{
    public const string CardId = "PYRE";
    public const int Cost = 2;
    public const int BaseEnergy = 1;
    public const decimal EnergyUpgrade = 1m;
    public const string PyrePowerId = "PYRE_POWER";

    public Pyre()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
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
        PowerCmd.Apply(cs, PyrePowerId, owner, card.Vars.Energy.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
