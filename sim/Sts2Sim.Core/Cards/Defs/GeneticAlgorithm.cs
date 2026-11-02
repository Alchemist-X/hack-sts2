using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class GeneticAlgorithm : CardDef
{
    public const string CardId = "GENETIC_ALGORITHM";
    public const int Cost = 1;
    public const string IncreaseKey = "Increase";
    public const int InitialBlock = 1;
    public const decimal BaseIncrease = 3m;
    public const decimal IncreaseUpgrade = 1m;
    public const ushort StateTag = 0xCB60;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public GeneticAlgorithm()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(InitialBlock, ValueProp.Move),
        new IntVar(IncreaseKey, BaseIncrease)
    };

    public override CardExtraState? CreateExtraState() => new GeneticAlgorithmState();

    public static int GetCurrentBlock(CardInstance card) => ((GeneticAlgorithmState)card.Extra!).CurrentBlock;

    public static int GetIncreasedBlock(CardInstance card) => ((GeneticAlgorithmState)card.Extra!).IncreasedBlock;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CreatureCmd.GainBlock(cs, card.Owner, card.Vars.Block, play);
        int intValue = card.Vars[IncreaseKey].IntValue;
        BuffFromPlay(card, intValue);
        var deckVersion = card.FindDeckVersion();
        if (deckVersion != null && deckVersion.Def is GeneticAlgorithm)
        {
            BuffFromPlay(deckVersion, intValue);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[IncreaseKey].UpgradeValueBy(IncreaseUpgrade);
    }

    public override void AfterDowngraded(CardInstance card)
    {
        UpdateBlock(card);
    }

    private static void BuffFromPlay(CardInstance card, int extraBlock)
    {
        ((GeneticAlgorithmState)card.Extra!).IncreasedBlock += extraBlock;
        UpdateBlock(card);
    }

    private static void UpdateBlock(CardInstance card)
    {
        var state = (GeneticAlgorithmState)card.Extra!;
        state.CurrentBlock = InitialBlock + state.IncreasedBlock;
        card.Vars.Block.BaseValue = state.CurrentBlock;
    }

    private sealed class GeneticAlgorithmState : CardExtraState
    {
        public int CurrentBlock { get; set; } = InitialBlock;

        public int IncreasedBlock { get; set; }

        public override CardExtraState DeepClone() => (GeneticAlgorithmState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(CurrentBlock);
            w.WriteInt32(IncreasedBlock);
        }
    }
}
