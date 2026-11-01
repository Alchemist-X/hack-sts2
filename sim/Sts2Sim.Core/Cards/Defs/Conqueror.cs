namespace Sts2Sim.Core.Cards;

public sealed class Conqueror : CardDef
{
    public const string CardId = "CONQUEROR";
    public const int Cost = 1;
    public const string ConquerorPowerId = "CONQUEROR_POWER";
    public const int BaseForge = 3;
    public const decimal ConquerorAmount = 1m;
    public const decimal ForgeUpgrade = 2m;

    public Conqueror()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var conqueror = play.Card;
        var owner = conqueror.Owner;
        ForgeCmd.Forge(cs, conqueror.Vars.Forge.IntValue, owner, null);
        PowerCmd.Apply(cs, PowerRegistry.Create(ConquerorPowerId), target, ConquerorAmount, owner, conqueror);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
