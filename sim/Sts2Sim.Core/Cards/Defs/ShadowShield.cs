namespace Sts2Sim.Core.Cards;

public sealed class ShadowShield : CardDef
{
    public const string CardId = "SHADOW_SHIELD";
    public const int Cost = 2;
    public const decimal BaseBlock = 11m;
    public const decimal BlockUpgrade = 4m;

    public ShadowShield()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var shadowShield = play.Card;
        var owner = shadowShield.Owner;
        CreatureCmd.GainBlock(cs, owner, shadowShield.Vars.Block, play);
        OrbCmd.Channel<DarkOrb>(cs, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
