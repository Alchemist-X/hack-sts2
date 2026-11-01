namespace Sts2Sim.Core.Cards;

public sealed class Hang : CardDef
{
    public const string CardId = "HANG";
    public const int Cost = 1;
    public const string HangPowerId = "HANG_POWER";
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 3m;
    public const int MinHangApplied = 2;
    public const int MaxPowerAmount = 999999999;

    public Hang()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
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
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        int powerAmount = target.GetPower(HangPowerId)?.Amount ?? 0;
        int amount = Math.Max(MinHangApplied, powerAmount);
        if (powerAmount + amount > MaxPowerAmount)
        {
            amount = Math.Max(0, MaxPowerAmount - powerAmount);
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(HangPowerId), target, amount, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
