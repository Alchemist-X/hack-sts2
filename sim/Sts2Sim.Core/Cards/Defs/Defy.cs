using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Defy : CardDef
{
    public const string CardId = "DEFY";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const decimal BaseWeak = 1m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Defy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var defy = play.Card;
        var owner = defy.Owner;
        CreatureCmd.GainBlock(cs, owner, defy.Vars.Block, play);
        PowerCmd.Apply<WeakPower>(cs, target, defy.Vars.Weak.BaseValue, owner, defy);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
