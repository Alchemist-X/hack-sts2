using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Debt : CardDef
{
    public const string CardId = "DEBT";
    public const int Cost = -1;
    public const int BaseGold = 10;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Debt()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(BaseGold)
    };

    public override bool HasTurnEndInHandEffect => true;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        var owner = card.Owner;
        int num2 = Math.Min(card.Vars[GoldVar.DefaultName].IntValue, owner.Gold);
        PlayerCmd.LoseGold(cs, num2, owner);
    }
}
