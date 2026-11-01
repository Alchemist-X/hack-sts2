using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SignalBoost : CardDef
{
    public const string CardId = "SIGNAL_BOOST";
    public const int Cost = 1;
    public const string PowerVarName = "SignalBoostPower";
    public const decimal BasePower = 1m;
    public const int CostUpgrade = -1;
    public const string SignalBoostPowerId = "SIGNAL_BOOST_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public SignalBoost()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, SignalBoostPowerId, owner, card.Vars[PowerVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
