using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BigBang : CardDef
{
    public const string CardId = "BIG_BANG";
    public const int Cost = 0;
    public const int BaseCards = 1;
    public const int BaseEnergy = 1;
    public const int BaseStars = 1;
    public const int BaseForge = 5;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public BigBang()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new EnergyVar(BaseEnergy),
        new StarsVar(BaseStars),
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bigBang = play.Card;
        var owner = bigBang.Owner;
        CardPileCmd.Draw(cs, bigBang.Vars.Cards.BaseValue, owner);
        PlayerCmd.GainStars(cs, bigBang.Vars.Stars.BaseValue, owner);
        PlayerCmd.GainEnergy(cs, bigBang.Vars.Energy.IntValue, owner);
        ForgeCmd.Forge(cs, bigBang.Vars.Forge.IntValue, owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Innate);
    }
}
