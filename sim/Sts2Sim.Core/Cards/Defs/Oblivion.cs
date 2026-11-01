namespace Sts2Sim.Core.Cards;

public sealed class Oblivion : CardDef
{
    public const string CardId = "OBLIVION";
    public const int Cost = 0;
    public const string OblivionPowerId = "OBLIVION_POWER";
    public const decimal BaseDoom = 3m;
    public const decimal DoomUpgrade = 1m;

    public Oblivion()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DoomName, BaseDoom)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        PowerCmd.Apply(cs, PowerRegistry.Create(OblivionPowerId), target, card.Vars.Doom.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Doom.UpgradeValueBy(DoomUpgrade);
    }
}
