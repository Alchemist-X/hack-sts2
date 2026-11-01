namespace Sts2Sim.Core.Cards;

public sealed class ManifestAuthority : CardDef
{
    public const string CardId = "MANIFEST_AUTHORITY";
    public const int Cost = 1;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 1m;
    public const int GeneratedCount = 1;

    public ManifestAuthority()
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
        var manifestAuthority = play.Card;
        var owner = manifestAuthority.Owner;
        var block = manifestAuthority.Vars.Block;
        CreatureCmd.GainBlock(cs, owner, block.BaseValue, block.Props, play);
        var rs = cs.Run;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        var cardModel = CardFactory.GetDistinctForCombat(cs, owner, options, GeneratedCount, rs.Rng.CombatCardGeneration).FirstOrDefault();
        if (cardModel != null)
        {
            if (manifestAuthority.IsUpgraded)
            {
                CardCmd.Upgrade(cs, cardModel);
            }
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
