using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Tactician : CardDef
{
    public const string CardId = "TACTICIAN";
    public const int Cost = 3;
    public const int BaseEnergy = 1;
    public const decimal EnergyUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public Tactician()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var tactician = play.Card;
        PlayerCmd.GainEnergy(cs, tactician.Vars.Energy.IntValue, tactician.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
