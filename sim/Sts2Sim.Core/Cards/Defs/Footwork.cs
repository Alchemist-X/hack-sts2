namespace Sts2Sim.Core.Cards;

public sealed class Footwork : CardDef
{
    public const string CardId = "FOOTWORK";
    public const int Cost = 1;
    public const decimal BaseDexterity = 2m;
    public const decimal DexterityUpgrade = 1m;

    public Footwork()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<DexterityPower>(BaseDexterity)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var footwork = play.Card;
        var owner = footwork.Owner;
        PowerCmd.Apply<DexterityPower>(cs, owner, footwork.Vars.Dexterity.BaseValue, owner, footwork);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Dexterity.UpgradeValueBy(DexterityUpgrade);
    }
}
