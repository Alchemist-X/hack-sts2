namespace Sts2Sim.Core.Cards;

public sealed class SpiritOfAsh : CardDef
{
    public const string CardId = "SPIRIT_OF_ASH";
    public const int Cost = 1;
    public const string BlockOnExhaustKey = "BlockOnExhaust";
    public const decimal BaseBlockOnExhaust = 4m;
    public const decimal BlockOnExhaustUpgrade = 1m;
    public const string SpiritOfAshPowerId = "SPIRIT_OF_ASH_POWER";

    public SpiritOfAsh()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(BlockOnExhaustKey, BaseBlockOnExhaust)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, SpiritOfAshPowerId, owner, card.Vars[BlockOnExhaustKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[BlockOnExhaustKey].UpgradeValueBy(BlockOnExhaustUpgrade);
    }
}
