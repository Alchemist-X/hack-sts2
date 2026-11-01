using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class LeadingStrike : CardDef
{
    public const string CardId = "LEADING_STRIKE";
    public const int Cost = 1;
    public const string ShivsKey = "Shivs";
    public const int BaseShivs = 2;
    public const decimal BaseDamage = 3m;
    public const decimal DamageUpgrade = 3m;
    public const string ShivId = "SHIV";

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public LeadingStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(ShivsKey, BaseShivs),
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var leadingStrike = play.Card;
        DamageCmd.Attack(cs, leadingStrike.Vars.Damage.BaseValue).FromCard(leadingStrike, play).Targeting(target).Execute();
        for (int i = 0; i < leadingStrike.Vars[ShivsKey].IntValue; i++)
        {
            CreateShivInHand(cs, leadingStrike.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private static void CreateShivInHand(CombatState cs, PlayerState owner)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shiv = cs.CreateCard(ShivId, owner);
        CardPileCmd.AddGeneratedCardsToCombat(cs, new[] { shiv }, PileType.Hand, owner);
    }
}
