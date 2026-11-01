namespace Sts2Sim.Core.Cards;

public sealed class WroughtInWar : CardDef
{
    public const string CardId = "WROUGHT_IN_WAR";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const int BaseForge = 7;
    public const decimal DamageUpgrade = 2m;
    public const decimal ForgeUpgrade = 2m;

    public WroughtInWar()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var wroughtInWar = play.Card;
        DamageCmd.Attack(cs, wroughtInWar.Vars.Damage.BaseValue).FromCard(wroughtInWar, play).Targeting(target).Execute();
        ForgeCmd.Forge(cs, wroughtInWar.Vars.Forge.IntValue, wroughtInWar.Owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
