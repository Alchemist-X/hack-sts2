namespace Sts2Sim.Core.Cards;

public sealed class SummonForth : CardDef
{
    public const string CardId = "SUMMON_FORTH";
    public const int Cost = 1;
    public const int BaseForge = 8;
    public const decimal ForgeUpgrade = 3m;
    public const string SovereignBladeId = "SOVEREIGN_BLADE";

    public SummonForth()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var summonForth = play.Card;
        var owner = summonForth.Owner;
        var cards = new List<CardInstance>();
        var piles = owner.AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            var pile = piles[i];
            if (pile.Type == PileType.Hand)
            {
                continue;
            }
            var pileCards = pile.Cards;
            for (int j = 0; j < pileCards.Count; j++)
            {
                if (string.Equals(pileCards[j].Id, SovereignBladeId, StringComparison.Ordinal))
                {
                    cards.Add(pileCards[j]);
                }
            }
        }
        CardPileCmd.Add(cs, cards, PileType.Hand);
        ForgeCmd.Forge(cs, summonForth.Vars.Forge.IntValue, owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
