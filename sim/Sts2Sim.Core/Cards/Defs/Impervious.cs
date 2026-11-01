using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Impervious : CardDef
{
    public const string CardId = "IMPERVIOUS";
    public const int Cost = 2;
    public const decimal BaseBlock = 30m;
    public const decimal BlockUpgrade = 10m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Impervious()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var impervious = play.Card;
        CreatureCmd.GainBlock(cs, impervious.Owner, impervious.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
