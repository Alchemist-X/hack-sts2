namespace Sts2Sim.Core.Run;

public sealed class EndlessConveyor : EventModel
{
    public const string EventId = "ENDLESS_CONVEYOR";

    public const int Gold = 40;

    public const int GoldenFyshGold = 75;

    public const int ClamRollHeal = 10;

    public const int CaviarMaxHp = 4;

    public const int MinGold = 120;

    public const string FeedingFrenzyId = "FEEDING_FRENZY";

    public const string Caviar = "CAVIAR";

    public const string SpicySnappy = "SPICY_SNAPPY";

    public const string JellyLiver = "JELLY_LIVER";

    public const string FriedEel = "FRIED_EEL";

    public const string SuspiciousCondiment = "SUSPICIOUS_CONDIMENT";

    public const string ClamRoll = "CLAM_ROLL";

    public const string GoldenFysh = "GOLDEN_FYSH";

    public const string SeapunkSalad = "SEAPUNK_SALAD";

    private string _lastDishId = "";

    private int _numOfGrabs;

    private string? _currentDishId;

    public override string Id => EventId;

    public string? CurrentDishId => _currentDishId;

    public int NumOfGrabs => _numOfGrabs;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Gold < MinGold)
            {
                return false;
            }
        }
        return true;
    }

    public override void CalculateVars(RunState rs)
    {
        RollDish();
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        GenerateGrabSomethingOffTheBeltOption(),
        new EventOption("ENDLESS_CONVEYOR.pages.INITIAL.options.OBSERVE_CHEF", ObserveChef)
    };

    private EventOption GenerateGrabSomethingOffTheBeltOption()
    {
        if (EventKit3456.OwnerOf(this).Gold >= Gold)
        {
            return new EventOption("ENDLESS_CONVEYOR.pages.ALL.options." + _currentDishId, GrabSomethingOffTheBelt);
        }
        return new EventOption("ENDLESS_CONVEYOR.pages.ALL.options.LOCKED", null);
    }

    private void GrabSomethingOffTheBelt(RunState rs)
    {
        if (_currentDishId != GoldenFysh)
        {
            PlayerCmd.LoseGold(rs, Gold, EventKit3456.OwnerOf(this), GoldLossType.Spent);
        }
        RunDish(rs, _currentDishId!);
        RollDish();
        SetEventState("ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.description", new[]
        {
            GenerateGrabSomethingOffTheBeltOption(),
            new EventOption("ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE", Leave)
        });
    }

    private void RunDish(RunState rs, string dishId)
    {
        var owner = EventKit3456.OwnerOf(this);
        switch (dishId)
        {
            case Caviar:
                EventKit3456.GainMaxHp(rs, owner, CaviarMaxHp);
                break;
            case ClamRoll:
                EventKit3456.Heal(owner, ClamRollHeal);
                break;
            case FriedEel:
                {
                    var options = CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.Colorless });
                    var card = CardFactory.CreateForReward(rs, owner, 1, options).First().Card;
                    CardPileCmd.Add(rs, card, PileType.Deck);
                    break;
                }
            case GoldenFysh:
                PlayerCmd.GainGold(rs, GoldenFyshGold, owner);
                break;
            case JellyLiver:
                {
                    var selected = EventKit3456.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), "EndlessConveyor.JellyLiver", Id);
                    if (selected.Count > 0)
                    {
                        EventKit3456.TransformToRandomInDeck(rs, selected[0], EventKit3456.RngOf(this));
                    }
                    break;
                }
            case SeapunkSalad:
                CardPileCmd.Add(rs, EventKit3456.CreateCard(rs, FeedingFrenzyId, owner), PileType.Deck);
                break;
            case SuspiciousCondiment:
                {
                    var items = PotionPools.ForCharacter(owner.CharacterId).GetUnlockedPotions(rs.Unlocks)
                        .Concat(PotionPools.Get(PotionPools.SharedPotionPool).GetUnlockedPotions(rs.Unlocks));
                    var canonical = RewardRngs.Rewards(owner).NextItem(items);
                    if (canonical != null)
                    {
                        var potion = EventKit3456.CreatePotion(rs, canonical.Id);
                        var rewards = new List<Reward>(1);
                        if (potion != null)
                        {
                            rewards.Add(new PotionReward(potion, owner));
                        }
                        RewardsCmd.OfferCustom(rs, owner, rewards);
                    }
                    break;
                }
            case SpicySnappy:
                {
                    var upgradable = owner.Deck.Where(c => c.IsUpgradable).ToList();
                    if (upgradable.Count != 0)
                    {
                        CardCmd.Upgrade(null, EventKit3456.RngOf(this).NextItem((IEnumerable<CardInstance>)upgradable)!);
                    }
                    break;
                }
            default:
                throw new InvalidOperationException("EndlessConveyor: unknown dish " + dishId);
        }
    }

    private void RollDish()
    {
        var owner = EventKit3456.OwnerOf(this);
        var rng = EventKit3456.RngOf(this);
        _numOfGrabs++;
        if (_numOfGrabs % 5 == 0)
        {
            _lastDishId = SeapunkSalad;
            _currentDishId = SeapunkSalad;
            return;
        }
        var dishes = new List<(string Id, float Weight)>
        {
            (Caviar, 6f),
            (SpicySnappy, 3f),
            (JellyLiver, 3f),
            (FriedEel, 3f)
        };
        if (owner.HasOpenPotionSlot)
        {
            dishes.Add((SuspiciousCondiment, 3f));
        }
        if (owner.CurrentHp != owner.MaxHp)
        {
            dishes.Add((ClamRoll, 6f));
        }
        if (_numOfGrabs > 1)
        {
            dishes.Add((GoldenFysh, 1f));
        }
        var last = _lastDishId;
        dishes.RemoveAll(d => d.Id == last);
        float total = 0f;
        for (int i = 0; i < dishes.Count; i++)
        {
            total += dishes[i].Weight;
        }
        float roll = rng.NextFloat() * total;
        float acc = 0f;
        for (int i = 0; i < dishes.Count; i++)
        {
            acc += dishes[i].Weight;
            if (roll < acc)
            {
                _lastDishId = dishes[i].Id;
                _currentDishId = dishes[i].Id;
                break;
            }
        }
    }

    private void ObserveChef(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var upgradable = owner.Deck.Where(c => c.IsUpgradable).ToArray();
        if (upgradable.Length > 0)
        {
            CardCmd.Upgrade(null, EventKit3456.RngOf(this).NextItem((IEnumerable<CardInstance>)upgradable)!);
        }
        SetEventFinished("ENDLESS_CONVEYOR.pages.OBSERVE_CHEF.description");
    }

    private void Leave(RunState rs)
    {
        SetEventFinished("ENDLESS_CONVEYOR.pages.LEAVE.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteString(_lastDishId);
        w.WriteInt32(_numOfGrabs);
        w.WriteString(_currentDishId);
    }
}
