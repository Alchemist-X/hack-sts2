namespace Sts2Sim.Core.Cards;

public sealed class Catastrophe : CardDef
{
    public const string CardId = "CATASTROPHE";
    public const int Cost = 2;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    public Catastrophe()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var catastrophe = play.Card;
        var owner = catastrophe.Owner;
        for (int i = 0; i < catastrophe.Vars.Cards.IntValue; i++)
        {
            var drawCards = owner.DrawPile.Cards;
            var playable = new List<CardInstance>(drawCards.Count);
            for (int j = 0; j < drawCards.Count; j++)
            {
                if (!drawCards[j].HasKeyword(CardKeyword.Unplayable))
                {
                    playable.Add(drawCards[j]);
                }
            }
            var cardModel = playable.StableShuffle(cs.Rng.Shuffle).FirstOrDefault();
            if (cardModel == null)
            {
                cardModel = new List<CardInstance>(owner.DrawPile.Cards).StableShuffle(cs.Rng.Shuffle).FirstOrDefault();
            }
            if (cardModel != null)
            {
                CardCmd.AutoPlay(cs, cardModel, null);
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
