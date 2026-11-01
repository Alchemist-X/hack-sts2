using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Apparition : CardDef
{
    public const string CardId = "APPARITION";
    public const int Cost = 1;
    public const string IntangiblePowerId = "INTANGIBLE_POWER";
    public const string PowerVarName = "IntangiblePower";
    public const decimal BaseIntangible = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal, CardKeyword.Exhaust });

    public Apparition()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseIntangible)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var apparition = play.Card;
        var owner = apparition.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(IntangiblePowerId), owner, apparition.Vars[PowerVarName].BaseValue, owner, apparition);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Ethereal);
    }
}
