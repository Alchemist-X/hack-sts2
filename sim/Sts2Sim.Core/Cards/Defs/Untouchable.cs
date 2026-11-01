using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Untouchable : CardDef
{
    public const string CardId = "UNTOUCHABLE";
    public const int Cost = 2;
    public const decimal BaseBlock = 6m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public Untouchable()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var untouchable = play.Card;
        CreatureCmd.GainBlock(cs, untouchable.Owner, untouchable.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
