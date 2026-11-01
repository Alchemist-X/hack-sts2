namespace Sts2Sim.Core.Cards;

public sealed class Unrelenting : CardDef
{
    public const string CardId = "UNRELENTING";
    public const int Cost = 2;
    public const decimal BaseDamage = 14m;
    public const decimal FreeAttackAmount = 1m;
    public const decimal DamageUpgrade = 6m;
    public const string FreeAttackPowerId = "FREE_ATTACK_POWER";

    public Unrelenting()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var owner = card.Owner;
        PowerCmd.Apply(cs, FreeAttackPowerId, owner, FreeAttackAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
