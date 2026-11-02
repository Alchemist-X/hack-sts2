using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SpoilsMap : CardDef
{
    public const string QuestOverlaySite = "SPOILS_MAP: AfterMapGenerated MapPoint.AddQuest (Models.Cards/SpoilsMap.cs:195-198) is a map-UI marker; the sim's ActMap quest overlay is copy-on-write and RunLoop.GenerateMap assigns rs.Map after the hook, so the marker is not added";

    public const string CardId = "SPOILS_MAP";
    public const int Cost = -1;
    public const int BaseGold = 600;
    public const int NoActIndex = -1;
    public const int CreatedSpoilsActIndex = 1;
    public const ushort StateTag = 0xCB64;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public SpoilsMap()
        : base(CardId, Cost, CardType.Quest, CardRarity.Quest, TargetType.Self)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(BaseGold)
    };

    public override CardExtraState? CreateExtraState() => new SpoilsMapState();

    public override AbstractModel? CreateListener(CardInstance card) => new SpoilsMapListener(card);

    public static int GetSpoilsActIndex(CardInstance card) => ((SpoilsMapState)card.Extra!).SpoilsActIndex;

    public static MapCoord? GetSpoilsCoord(CardInstance card) => ((SpoilsMapState)card.Extra!).SpoilsCoord;

    public override void AfterCreated(CardInstance card)
    {
        ((SpoilsMapState)card.Extra!).SpoilsActIndex = CreatedSpoilsActIndex;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public static int OnQuestComplete(RunState rs, CardInstance card)
    {
        PlayerCmd.GainGold(rs, card.Vars[GoldVar.DefaultName].BaseValue, card.Owner);
        PlayerCmd.CompleteQuest(rs, card);
        CardPileCmd.RemoveFromDeck(rs, card);
        return card.Vars[GoldVar.DefaultName].IntValue;
    }

    private sealed class SpoilsMapState : CardExtraState
    {
        public int SpoilsActIndex { get; set; } = NoActIndex;

        public MapCoord? SpoilsCoord { get; set; }

        public override CardExtraState DeepClone() => (SpoilsMapState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(SpoilsActIndex);
            w.WriteBool(SpoilsCoord.HasValue);
            if (SpoilsCoord.HasValue)
            {
                SpoilsCoord.Value.WriteCanonical(w);
            }
        }
    }

    private sealed class SpoilsMapListener : CardListener
    {
        public SpoilsMapListener(CardInstance card)
            : base(card)
        {
        }

        private SpoilsMapState State => (SpoilsMapState)Card.Extra!;

        public override AbstractModel CloneFor(CardInstance newOwner) => new SpoilsMapListener(newOwner);

        public override ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)
        {
            if (actIndex != State.SpoilsActIndex)
            {
                return map;
            }
            if (!Card.IsInDeck)
            {
                return map;
            }
            return new SpoilsActMap(rs);
        }

        public override ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)
        {
            if (actIndex != State.SpoilsActIndex)
            {
                return map;
            }
            if (!Card.IsInDeck)
            {
                return map;
            }
            var mapPoint = map.GetAllMapPoints().FirstOrDefault(p => p.PointType == MapPointType.Treasure);
            if (mapPoint != null)
            {
                State.SpoilsCoord = mapPoint.coord;
            }
            return map;
        }

        public override void AfterMapGenerated(RunState rs, ActMap map, int actIndex)
        {
            if (!Card.IsInDeck)
            {
                return;
            }
            if (actIndex != State.SpoilsActIndex)
            {
                return;
            }
            if (State.SpoilsCoord.HasValue && map.HasPoint(State.SpoilsCoord.Value))
            {
                rs.Fidelity.Record(FidelityFlag.ApproximatedRule, QuestOverlaySite);
            }
        }

        public override void BeforeCardRemoved(RunState rs, CardInstance card)
        {
            if (!ReferenceEquals(card, Card))
            {
                return;
            }
            if (State.SpoilsActIndex != rs.CurrentActIndex)
            {
                return;
            }
            if (!State.SpoilsCoord.HasValue)
            {
                return;
            }
            if (rs.Map != null)
            {
                rs.Map = rs.Map.WithQuestRemoved(State.SpoilsCoord.Value, CardId);
            }
        }
    }
}
