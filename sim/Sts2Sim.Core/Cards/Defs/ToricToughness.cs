namespace Sts2Sim.Core.Cards;

public sealed class ToricToughness : CardDef
{
    public const string CardId = "TORIC_TOUGHNESS";
    public const int Cost = 2;
    public const string TurnsKey = "Turns";
    public const decimal BaseTurns = 2m;
    public const decimal BaseBlock = 5m;
    public const decimal BlockUpgrade = 2m;
    public const string SetBlockMember = "SetBlock";
    public const string ToricToughnessPowerId = "TORIC_TOUGHNESS_POWER";

    public ToricToughness()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(TurnsKey, BaseTurns),
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var blockAmount = CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        var toricToughnessPower = PowerCmd.Apply(cs, ToricToughnessPowerId, owner, card.Vars[TurnsKey].BaseValue, owner, card);
        if (toricToughnessPower != null)
        {
            toricToughnessPower.GetType().GetMethod(SetBlockMember, new[] { typeof(decimal) })!.Invoke(toricToughnessPower, new object[] { blockAmount });
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
