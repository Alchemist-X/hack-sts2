namespace Sts2Sim.Core.Cards;

public sealed class AstralPulse : CardDef
{
    public const string CardId = "ASTRAL_PULSE";
    public const int Cost = 0;
    public const int StarCost = 3;
    public const decimal BaseDamage = 6m;
    public const int HitCount = 2;
    public const decimal DamageUpgrade = 2m;

    public AstralPulse()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var astralPulse = play.Card;
        DamageCmd.Attack(cs, astralPulse.Vars.Damage.BaseValue).FromCard(astralPulse, play).TargetingAllOpponents().WithHitCount(HitCount).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
