using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class ForgottenRitual : CardDef
{
    public const string CardId = "FORGOTTEN_RITUAL";
    public const int Cost = 1;
    public const int BaseEnergy = 3;
    public const decimal EnergyUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public ForgottenRitual()
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
        var forgottenRitual = play.Card;
        PlayerCmd.GainEnergy(cs, forgottenRitual.Vars.Energy.IntValue, forgottenRitual.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
