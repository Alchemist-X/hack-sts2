using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Hotfix : CardDef
{
    public const string CardId = "HOTFIX";
    public const int Cost = 0;
    public const decimal BaseFocus = 2m;
    public const string FocusVarName = "FocusPower";
    public const string HotfixPowerId = "HOTFIX_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Hotfix()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<FocusPower>(BaseFocus)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hotfix = play.Card;
        var owner = hotfix.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(HotfixPowerId), owner, hotfix.Vars[FocusVarName].BaseValue, owner, hotfix);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
