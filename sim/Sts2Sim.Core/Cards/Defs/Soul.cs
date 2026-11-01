using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Soul : CardDef
{
    public const string CardId = "SOUL";
    public const int Cost = 0;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Soul()
        : base(CardId, Cost, CardType.Skill, CardRarity.Token, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public static IReadOnlyList<CardInstance> Create(CombatState cs, PlayerState owner, int amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardRegistry.Get(CardId);
        var souls = new List<CardInstance>(Math.Max(0, amount));
        for (int i = 0; i < amount; i++)
        {
            souls.Add(cs.CreateCard(def, owner));
        }
        return souls;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var soul = play.Card;
        CardPileCmd.Draw(cs, soul.Vars.Cards.BaseValue, soul.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
