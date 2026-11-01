namespace Sts2Sim.Core.Cards;

public sealed class EvilEye : CardDef
{
    public const string CardId = "EVIL_EYE";
    public const int Cost = 1;
    public const decimal BaseBlock = 8m;
    public const decimal BlockUpgrade = 3m;

    public EvilEye()
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
        var evilEye = play.Card;
        var owner = evilEye.Owner;
        int blockGains = !WasCardExhaustedThisTurn(cs, evilEye) ? 1 : 2;
        for (int i = 0; i < blockGains; i++)
        {
            CreatureCmd.GainBlock(cs, owner, evilEye.Vars.Block, play);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }

    private static bool WasCardExhaustedThisTurn(CombatState cs, CardInstance card)
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.AnyThisTurn<CardExhaustedEntry>(cs, e => e.Stamp.ActorId == ownerId);
    }
}
