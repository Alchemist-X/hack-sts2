namespace Sts2Sim.Core.Cards;

public sealed class Anticipate : CardDef
{
    public const string CardId = "ANTICIPATE";
    public const int Cost = 0;
    public const string AnticipatePowerId = "ANTICIPATE_POWER";
    public const decimal BaseDexterity = 2m;
    public const decimal DexterityUpgrade = 2m;

    public Anticipate()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<DexterityPower>(BaseDexterity)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var anticipate = play.Card;
        var owner = anticipate.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(AnticipatePowerId), owner, anticipate.Vars.Dexterity.BaseValue, owner, anticipate);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Dexterity.UpgradeValueBy(DexterityUpgrade);
    }
}
