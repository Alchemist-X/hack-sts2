namespace Sts2Sim.Core.Cards;

public sealed class CloakAndDagger : CardDef
{
    public const string CardId = "CLOAK_AND_DAGGER";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;
    public const string ShivId = "SHIV";

    public CloakAndDagger()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var cloakAndDagger = play.Card;
        var owner = cloakAndDagger.Owner;
        CreatureCmd.GainBlock(cs, owner, cloakAndDagger.Vars.Block, play);
        for (int i = 0; i < cloakAndDagger.Vars.Cards.IntValue; i++)
        {
            CreateShivInHand(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    private static void CreateShivInHand(CombatState cs, PlayerState owner)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shivs = new List<CardInstance> { cs.CreateCard(ShivId, owner) };
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
    }
}
