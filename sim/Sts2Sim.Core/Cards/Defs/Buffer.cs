namespace Sts2Sim.Core.Cards;

public sealed class Buffer : CardDef
{
    public const string CardId = "BUFFER";
    public const int Cost = 2;
    public const string BufferPowerId = "BUFFER_POWER";
    public const string PowerVarName = "BufferPower";
    public const decimal BaseBuffer = 1m;
    public const decimal BufferUpgrade = 1m;

    public Buffer()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseBuffer)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var buffer = play.Card;
        var owner = buffer.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(BufferPowerId), owner, buffer.Vars[PowerVarName].BaseValue, owner, buffer);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(BufferUpgrade);
    }
}
