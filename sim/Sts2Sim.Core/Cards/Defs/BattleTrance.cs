namespace Sts2Sim.Core.Cards;

public sealed class BattleTrance : CardDef
{
    public const string CardId = "BATTLE_TRANCE";
    public const int Cost = 0;
    public const string NoDrawPowerId = "NO_DRAW_POWER";
    public const int BaseCards = 3;
    public const decimal NoDrawAmount = 1m;
    public const decimal CardsUpgrade = 1m;

    public BattleTrance()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var battleTrance = play.Card;
        var owner = battleTrance.Owner;
        CardPileCmd.Draw(cs, battleTrance.Vars.Cards.BaseValue, owner);
        PowerCmd.Apply(cs, PowerRegistry.Create(NoDrawPowerId), owner, NoDrawAmount, owner, battleTrance);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
