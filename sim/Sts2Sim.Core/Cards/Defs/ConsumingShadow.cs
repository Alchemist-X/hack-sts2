namespace Sts2Sim.Core.Cards;

public sealed class ConsumingShadow : CardDef
{
    public const string CardId = "CONSUMING_SHADOW";
    public const int Cost = 2;
    public const string ConsumingShadowPowerId = "CONSUMING_SHADOW_POWER";
    public const string ConsumingShadowPowerVar = "ConsumingShadowPower";
    public const int BaseRepeat = 2;
    public const decimal BaseConsumingShadow = 1m;
    public const decimal RepeatUpgrade = 1m;

    public ConsumingShadow()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat),
        new DynamicVar(ConsumingShadowPowerVar, BaseConsumingShadow)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var consumingShadow = play.Card;
        var owner = consumingShadow.Owner;
        for (int i = 0; i < consumingShadow.Vars.Repeat.IntValue; i++)
        {
            OrbCmd.Channel<DarkOrb>(cs, owner);
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(ConsumingShadowPowerId), owner, consumingShadow.Vars[ConsumingShadowPowerVar].BaseValue, owner, consumingShadow);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
