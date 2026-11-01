namespace Sts2Sim.Core.Cards;

public sealed class HandOfGreed : CardDef
{
    public const string CardId = "HAND_OF_GREED";
    public const int Cost = 2;
    public const int goldAmount = 20;
    public const string GoldKey = "Gold";
    public const decimal BaseDamage = 20m;
    public const decimal BaseGold = 20m;
    public const decimal DamageUpgrade = 5m;
    public const decimal GoldUpgrade = 5m;

    public HandOfGreed()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(GoldKey, BaseGold)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var handOfGreed = play.Card;
        bool shouldTriggerFatal = target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal());
        var attackCommand = DamageCmd.Attack(cs, handOfGreed.Vars.Damage.BaseValue).FromCard(handOfGreed, play).Targeting(target).Execute();
        if (shouldTriggerFatal && attackCommand.HitResults.SelectMany(r => r).Any(r => r.WasTargetKilled))
        {
            PlayerCmd.GainGold(cs, handOfGreed.Vars[GoldKey].IntValue, handOfGreed.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[GoldKey].UpgradeValueBy(GoldUpgrade);
    }
}
