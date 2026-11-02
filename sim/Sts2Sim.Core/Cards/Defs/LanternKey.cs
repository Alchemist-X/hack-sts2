using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class LanternKey : CardDef
{
    public const string CardId = "LANTERN_KEY";
    public const int Cost = -1;
    public const int GloryActIndex = 2;
    public const string WarHistorianRepyEventId = "WAR_HISTORIAN_REPY";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public LanternKey()
        : base(CardId, Cost, CardType.Quest, CardRarity.Quest, TargetType.Self)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override AbstractModel? CreateListener(CardInstance card) => new LanternKeyListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private sealed class LanternKeyListener : CardListener
    {
        public LanternKeyListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new LanternKeyListener(newOwner);

        public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)
        {
            if (GloryActIndex != rs.CurrentActIndex)
            {
                return roomTypes;
            }
            return new HashSet<RoomType> { RoomType.Event };
        }

        public override EventModel ModifyNextEvent(RunState rs, EventModel currentEvent)
        {
            if (GloryActIndex != rs.CurrentActIndex)
            {
                return currentEvent;
            }
            return EventRegistry.Create(WarHistorianRepyEventId);
        }
    }
}
