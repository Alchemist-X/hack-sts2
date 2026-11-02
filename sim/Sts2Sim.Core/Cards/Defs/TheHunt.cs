using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class TheHunt : CardDef
{
    public const string CardId = "THE_HUNT";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 5m;
    public const int RewardOptionCount = 3;
    public const string TheHuntPowerId = "THE_HUNT_POWER";
    public const decimal PowerAmount = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public TheHunt()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var theHunt = play.Card;
        var rs = cs.Run;
        if (RoomStack.CurrentRoom(rs) is not CombatRoom combatRoom)
        {
            return;
        }
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        bool shouldTriggerFatal = target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal(cs));
        var attackCommand = DamageCmd.Attack(cs, theHunt.Vars.Damage.BaseValue).FromCard(theHunt, play).Targeting(target).Execute();
        if (shouldTriggerFatal && attackCommand.Results.Any(r => r.WasTargetKilled))
        {
            var owner = theHunt.Owner;
            combatRoom.AddExtraReward(new CardReward(owner, CardCreationOptions.ForRoom(rs, owner, combatRoom.RoomType), RewardOptionCount));
            PowerCmd.Apply(cs, PowerRegistry.Create(TheHuntPowerId), owner, PowerAmount, owner, theHunt);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
