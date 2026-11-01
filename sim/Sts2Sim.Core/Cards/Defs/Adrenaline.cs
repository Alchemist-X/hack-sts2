using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Adrenaline : CardDef
{
    public const string CardId = "ADRENALINE";
    public const int Cost = 0;
    public const int BaseEnergy = 1;
    public const int BaseCards = 2;
    public const decimal EnergyUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Adrenaline()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PlayerCmd.GainEnergy(cs, card.Vars.Energy.IntValue, owner);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
