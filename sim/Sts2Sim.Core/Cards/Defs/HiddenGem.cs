namespace Sts2Sim.Core.Cards;

public sealed class HiddenGem : CardDef
{
    public const string CardId = "HIDDEN_GEM";
    public const int Cost = 1;
    public const string ReplayKey = "Replay";
    public const decimal BaseReplay = 2m;
    public const decimal ReplayUpgrade = 1m;

    public HiddenGem()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new IntVar(ReplayKey, BaseReplay)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hiddenGem = play.Card;
        var drawCards = new List<CardInstance>(hiddenGem.Owner.DrawPile.Cards);
        if (drawCards.Count == 0)
        {
            return;
        }
        var eligible = new List<CardInstance>(drawCards.Count);
        for (int i = 0; i < drawCards.Count; i++)
        {
            var c = drawCards[i];
            bool notUnplayable = !c.HasKeyword(CardKeyword.Unplayable);
            if (notUnplayable && c.Type != CardType.Curse && c.Type != CardType.Quest && c.GetEnchantedReplayCount() < 1)
            {
                eligible.Add(c);
            }
        }
        var preferred = new List<CardInstance>(eligible.Count);
        for (int i = 0; i < eligible.Count; i++)
        {
            var type = eligible[i].Type;
            if (type == CardType.Attack || type == CardType.Skill || type == CardType.Power)
            {
                preferred.Add(eligible[i]);
            }
        }
        var items = preferred.Count == 0 ? eligible : preferred;
        var cardModel = cs.Rng.CombatCardSelection.NextItem((IReadOnlyList<CardInstance>)items);
        if (cardModel != null)
        {
            cardModel.BaseReplayCount += hiddenGem.Vars[ReplayKey].IntValue;
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ReplayKey].UpgradeValueBy(ReplayUpgrade);
    }
}
