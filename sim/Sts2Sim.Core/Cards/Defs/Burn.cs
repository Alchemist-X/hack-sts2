using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Burn : CardDef
{
    public const string CardId = "BURN";
    public const int Cost = -1;
    public const decimal BaseDamage = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Burn()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override bool HasTurnEndInHandEffect => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Unpowered | ValueProp.Move)
    };

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        CreatureCmd.Damage(cs, card.Owner, card.Vars.Damage, card, null);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }
}
