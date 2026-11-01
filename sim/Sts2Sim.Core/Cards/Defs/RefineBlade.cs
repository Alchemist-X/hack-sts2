namespace Sts2Sim.Core.Cards;

public sealed class RefineBlade : CardDef
{
    public const string CardId = "REFINE_BLADE";
    public const int Cost = 1;
    public const int BaseForge = 8;
    public const int BaseEnergy = 1;
    public const decimal ForgeUpgrade = 4m;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";

    public RefineBlade()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var refineBlade = play.Card;
        var owner = refineBlade.Owner;
        ForgeCmd.Forge(cs, refineBlade.Vars.Forge.IntValue, owner, null);
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, refineBlade.Vars.Energy.BaseValue, owner, refineBlade);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
