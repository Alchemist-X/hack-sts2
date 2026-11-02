namespace Sts2Sim.Core.Cards;

public sealed class Taunt : CardDef
{
    public const string CardId = "TAUNT";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const decimal BaseVulnerable = 1m;
    public const decimal BlockUpgrade = 1m;
    public const decimal VulnerableUpgrade = 1m;

    public Taunt()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var taunt = play.Card;
        var owner = taunt.Owner;
        CreatureCmd.GainBlock(cs, owner, taunt.Vars.Block, play);
        PowerCmd.Apply<VulnerablePower>(cs, target, taunt.Vars.Vulnerable.BaseValue, owner, taunt);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
