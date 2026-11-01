using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Demesne : CardDef
{
    public const string CardId = "DEMESNE";
    public const int Cost = 3;
    public const string DemesnePowerId = "DEMESNE_POWER";
    public const int BaseEnergy = 1;
    public const int BaseCards = 1;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Demesne()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
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
        var demesne = play.Card;
        var owner = demesne.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(DemesnePowerId), owner, demesne.Vars.Cards.BaseValue, owner, demesne);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
