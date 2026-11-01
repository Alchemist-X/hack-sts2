namespace Sts2Sim.Core.Cards;

public sealed class Shadowmeld : CardDef
{
    public const string CardId = "SHADOWMELD";
    public const int Cost = 1;
    public const string PowerKey = "Power";
    public const decimal BasePower = 1m;
    public const int CostUpgrade = -1;
    public const string ShadowmeldPowerId = "SHADOWMELD_POWER";

    public Shadowmeld()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, ShadowmeldPowerId, owner, card.Vars[PowerKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
