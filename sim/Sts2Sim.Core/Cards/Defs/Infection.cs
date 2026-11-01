using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Infection : CardDef
{
    public const string CardId = "INFECTION";
    public const int Cost = -1;
    public const decimal BaseDamage = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Infection()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Unpowered | ValueProp.Move)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        CreatureCmd.Damage(cs, card.Owner, card.Vars.Damage, card, null);
    }
}
