using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Dowsing : CardDef
{
    public const string CardId = "DOWSING";
    public const int Cost = -1;
    public const int MaxRooms = 5;
    public const string RoomsKey = "Rooms";
    public const string AbundanceCardId = "ABUNDANCE";
    public const ushort StateTag = 0xCB63;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Dowsing()
        : base(CardId, Cost, CardType.Quest, CardRarity.Quest, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(RoomsKey, MaxRooms)
    };

    public override CardExtraState? CreateExtraState() => new DowsingState();

    public override AbstractModel? CreateListener(CardInstance card) => new DowsingListener(card);

    public static int GetRoomsEntered(CardInstance card) => ((DowsingState)card.Extra!).RoomsEntered;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private static void SetRoomsEntered(CardInstance card, int value)
    {
        ((DowsingState)card.Extra!).RoomsEntered = value;
        card.Vars[RoomsKey].BaseValue = MaxRooms - value;
    }

    private sealed class DowsingState : CardExtraState
    {
        public int RoomsEntered { get; set; }

        public override CardExtraState DeepClone() => (DowsingState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(RoomsEntered);
        }
    }

    private sealed class DowsingListener : CardListener
    {
        public DowsingListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new DowsingListener(newOwner);

        public override void BeforeRoomEntered(RunState rs, AbstractRoom room)
        {
            if (!Card.IsInDeck || rs.CurrentRooms.Count > 1)
            {
                return;
            }
            var coord = rs.CurrentMapCoord;
            var currentMapPoint = coord.HasValue ? rs.Map?.GetPoint(coord.Value) : null;
            if (currentMapPoint == null || currentMapPoint.PointType != MapPointType.Unknown)
            {
                return;
            }
            SetRoomsEntered(Card, GetRoomsEntered(Card) + 1);
            if (GetRoomsEntered(Card) >= MaxRooms)
            {
                PlayerCmd.CompleteQuest(rs, Card);
                CardCmd.TransformDeckCardTo(rs, Card, AbundanceCardId);
            }
        }
    }
}
