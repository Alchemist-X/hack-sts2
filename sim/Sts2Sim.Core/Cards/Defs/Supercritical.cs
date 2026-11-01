using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Supercritical : CardDef
{
    public const string CardId = "SUPERCRITICAL";
    public const int Cost = 0;
    public const int BaseEnergy = 4;
    public const decimal EnergyUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Supercritical()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var supercritical = play.Card;
        PlayerCmd.GainEnergy(cs, supercritical.Vars.Energy.IntValue, supercritical.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
