namespace Sts2Sim.Core.Cards;

public sealed class BeatDown : CardDef
{
    public const string CardId = "BEAT_DOWN";
    public const int Cost = 3;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;

    public BeatDown()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.RandomEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var beatDown = play.Card;
        var pile = beatDown.Owner.DiscardPile;
        var eligible = new List<CardInstance>();
        var cards = pile.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            if (c.Type == CardType.Attack && !c.HasKeyword(CardKeyword.Unplayable))
            {
                eligible.Add(c);
            }
        }
        var enumerable = eligible.StableShuffle(cs.Rng.Shuffle).Take(beatDown.Vars.Cards.IntValue);
        foreach (var current in enumerable)
        {
            if (CombatStatus.IsOverOrEnding(cs))
            {
                break;
            }
            if (current.TargetType == TargetType.AnyEnemy)
            {
                var hittable = new List<MonsterState>(cs.Monsters.Count);
                cs.GetHittableEnemies(hittable);
                var target = cs.Rng.CombatTargets.NextItem(hittable);
                CardCmd.AutoPlay(cs, current, target);
            }
            else
            {
                CardCmd.AutoPlay(cs, current, null);
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
