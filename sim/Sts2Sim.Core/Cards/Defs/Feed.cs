using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Feed : CardDef
{
    public const string CardId = "FEED";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const decimal BaseMaxHp = 3m;
    public const decimal DamageUpgrade = 2m;
    public const decimal MaxHpUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Feed()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new MaxHpVar(BaseMaxHp)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var feed = play.Card;
        bool shouldTriggerFatal = target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal());
        var attackCommand = DamageCmd.Attack(cs, feed.Vars.Damage.BaseValue).FromCard(feed, play).Targeting(target).Execute();
        if (shouldTriggerFatal && attackCommand.Results.Any(r => r.WasTargetKilled))
        {
            CreatureCmd.GainMaxHp(cs, feed.Owner, feed.Vars.MaxHp.IntValue);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.MaxHp.UpgradeValueBy(MaxHpUpgrade);
    }
}
