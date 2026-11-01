using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Fuel : CardDef
{
    public const string CardId = "FUEL";
    public const int Cost = 0;
    public const int BaseEnergy = 1;
    public const decimal EnergyUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Fuel()
        : base(CardId, Cost, CardType.Skill, CardRarity.Token, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var fuel = play.Card;
        PlayerCmd.GainEnergy(cs, fuel.Vars.Energy.BaseValue, fuel.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
