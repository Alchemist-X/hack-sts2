using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Wisp : CardDef
{
    public const string CardId = "WISP";
    public const int Cost = 0;
    public const int BaseEnergy = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Wisp()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var wisp = play.Card;
        PlayerCmd.GainEnergy(cs, wisp.Vars.Energy.IntValue, wisp.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
