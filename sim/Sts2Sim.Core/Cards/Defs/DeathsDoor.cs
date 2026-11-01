namespace Sts2Sim.Core.Cards;

public sealed class DeathsDoor : CardDef
{
    public const string CardId = "DEATHS_DOOR";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const int BaseRepeat = 2;
    public const decimal BlockUpgrade = 1m;
    public const string DoomPowerId = "DOOM_POWER";

    public DeathsDoor()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var deathsDoor = play.Card;
        var owner = deathsDoor.Owner;
        int blockGains = 1;
        if (WasDoomAppliedThisTurn(cs, deathsDoor))
        {
            blockGains += deathsDoor.Vars.Repeat.IntValue;
        }
        for (int i = 0; i < blockGains; i++)
        {
            CreatureCmd.GainBlock(cs, owner, deathsDoor.Vars.Block, play);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }

    private static bool WasDoomAppliedThisTurn(CombatState cs, CardInstance card)
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.AnyThisTurn<PowerReceivedEntry>(cs, e => e.PowerId == DoomPowerId && e.ApplierId == ownerId);
    }
}
