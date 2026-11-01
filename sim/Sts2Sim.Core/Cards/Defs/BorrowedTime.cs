namespace Sts2Sim.Core.Cards;

public sealed class BorrowedTime : CardDef
{
    public const string CardId = "BORROWED_TIME";
    public const int Cost = 1;
    public const string BorrowedTimePowerId = "BORROWED_TIME_POWER";
    public const string ExtraCostVarName = "ExtraCost";
    public const int BaseEnergy = 4;
    public const int BaseExtraCost = 1;
    public const decimal EnergyUpgrade = 2m;

    public BorrowedTime()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy),
        new EnergyVar(ExtraCostVarName, BaseExtraCost)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var borrowedTime = play.Card;
        var owner = borrowedTime.Owner;
        EnergyCmd.GainEnergy(cs, borrowedTime.Vars.Energy.BaseValue, owner);
        PowerCmd.Apply(cs, PowerRegistry.Create(BorrowedTimePowerId), owner, borrowedTime.Vars[ExtraCostVarName].BaseValue, owner, borrowedTime);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
