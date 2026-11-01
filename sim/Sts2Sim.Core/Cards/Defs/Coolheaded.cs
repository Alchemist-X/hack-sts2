namespace Sts2Sim.Core.Cards;

public sealed class Coolheaded : CardDef
{
    public const string CardId = "COOLHEADED";
    public const int Cost = 1;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;

    public Coolheaded()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var coolheaded = play.Card;
        OrbCmd.Channel<FrostOrb>(cs, coolheaded.Owner);
        CardPileCmd.Draw(cs, coolheaded.Vars.Cards.BaseValue, coolheaded.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
