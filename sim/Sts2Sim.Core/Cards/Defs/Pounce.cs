namespace Sts2Sim.Core.Cards;

public sealed class Pounce : CardDef
{
    public const string CardId = "POUNCE";
    public const int Cost = 2;
    public const string FreeSkillPowerId = "FREE_SKILL_POWER";
    public const decimal BaseDamage = 14m;
    public const decimal DamageUpgrade = 6m;
    public const decimal FreeSkillAmount = 1m;

    public Pounce()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(FreeSkillPowerId), owner, FreeSkillAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
