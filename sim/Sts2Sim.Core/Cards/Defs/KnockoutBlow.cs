namespace Sts2Sim.Core.Cards;

public sealed class KnockoutBlow : CardDef
{
    public const string CardId = "KNOCKOUT_BLOW";
    public const int Cost = 3;
    public const decimal BaseDamage = 30m;
    public const int BaseStars = 5;
    public const decimal DamageUpgrade = 8m;

    public KnockoutBlow()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var knockoutBlow = play.Card;
        var attackCommand = DamageCmd.Attack(cs, knockoutBlow.Vars.Damage.BaseValue).FromCard(knockoutBlow, play).Targeting(target).Execute();
        if (attackCommand.Results.Any(r => r.WasTargetKilled))
        {
            PlayerCmd.GainStars(cs, knockoutBlow.Vars.Stars.BaseValue, knockoutBlow.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
