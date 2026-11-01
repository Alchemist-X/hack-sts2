namespace Sts2Sim.Core.Cards;

public sealed class Furnace : CardDef
{
    public const string CardId = "FURNACE";
    public const int Cost = 1;
    public const string FurnacePowerId = "FURNACE_POWER";
    public const int BaseForge = 5;
    public const decimal ForgeUpgrade = 2m;

    public Furnace()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var furnace = play.Card;
        var owner = furnace.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FurnacePowerId), owner, furnace.Vars.Forge.BaseValue, owner, furnace);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
