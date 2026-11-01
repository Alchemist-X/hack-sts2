namespace Sts2Sim.Core.Cards;

public sealed class PoisonedStab : CardDef
{
    public const string CardId = "POISONED_STAB";
    public const int Cost = 1;
    public const string PoisonPowerId = "POISON_POWER";
    public const decimal BaseDamage = 6m;
    public const decimal BasePoison = 3m;
    public const decimal DamageUpgrade = 2m;
    public const decimal PoisonUpgrade = 1m;

    public PoisonedStab()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), target, card.Vars.Poison.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
    }
}
