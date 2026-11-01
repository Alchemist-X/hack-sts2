namespace Sts2Sim.Core.State;

public abstract class CreatureState : ICanonical
{
    private int _currentHp;
    private int _maxHp;
    private int _block;

    protected CreatureState(int combatId, CombatSide side, int currentHp, int maxHp)
    {
        CombatId = combatId;
        Side = side;
        _currentHp = currentHp;
        _maxHp = maxHp;
        Powers = new List<PowerModel>(4);
    }

    public int CombatId { get; }

    public CombatSide Side { get; set; }

    public int CurrentHp
    {
        get => _currentHp;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Current HP must be positive", nameof(value));
            }
            _currentHp = value;
        }
    }

    public int MaxHp
    {
        get => _maxHp;
        set => _maxHp = value;
    }

    public int Block
    {
        get => _block;
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Block must be positive", nameof(value));
            }
            _block = value;
        }
    }

    public List<PowerModel> Powers { get; }

    public bool IsAlive => _currentHp > 0;

    public bool IsDead => !IsAlive;

    public bool IsEnemy => Side == CombatSide.Enemy;

    public abstract bool IsPlayer { get; }

    public bool IsMonster => !IsPlayer;

    public T? GetPower<T>() where T : PowerModel
    {
        var powers = Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (powers[i] is T t)
            {
                return t;
            }
        }
        return null;
    }

    public PowerModel? GetPower(string powerId)
    {
        var powers = Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            if (powers[i].Id == powerId)
            {
                return powers[i];
            }
        }
        return null;
    }

    public bool HasPower<T>() where T : PowerModel => GetPower<T>() != null;

    public bool HasPower(string powerId) => GetPower(powerId) != null;

    public int GetPowerAmount<T>() where T : PowerModel => GetPower<T>()?.Amount ?? 0;

    public abstract CreatureState DeepCloneCreature();

    protected void CopyCreatureFieldsInto(CreatureState clone)
    {
        clone.Side = Side;
        clone._currentHp = _currentHp;
        clone._maxHp = _maxHp;
        clone._block = _block;
        var source = Powers;
        var target = clone.Powers;
        target.Clear();
        if (target.Capacity < source.Count)
        {
            target.Capacity = source.Count;
        }
        for (int i = 0; i < source.Count; i++)
        {
            target.Add(source[i].CloneFor(clone));
        }
    }

    protected void WriteCreatureCanonical(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.Creature);
        w.WriteInt32(CombatId);
        w.WriteInt32((int)Side);
        w.WriteInt32(_currentHp);
        w.WriteInt32(_maxHp);
        w.WriteInt32(_block);
        var powers = Powers;
        w.WriteCount(powers.Count);
        for (int i = 0; i < powers.Count; i++)
        {
            powers[i].WriteCanonical(w);
        }
    }

    public virtual void WriteCanonical(CanonicalWriter w) => WriteCreatureCanonical(w);
}

public sealed class PlayerState : CreatureState
{
    public const int DefaultPotionSlotCount = 3;

    private CardPile[]? _allPiles;

    public PlayerState(int combatId, string characterId, int currentHp, int maxHp, int maxEnergy)
        : base(combatId, CombatSide.Player, currentHp, maxHp)
    {
        CharacterId = characterId;
        BaseMaxEnergy = maxEnergy;
        TurnNumber = 1;
        IsActiveForHooks = currentHp > 0;
        DrawPile = new CardPile(PileType.Draw);
        Hand = new CardPile(PileType.Hand);
        DiscardPile = new CardPile(PileType.Discard);
        ExhaustPile = new CardPile(PileType.Exhaust);
        PlayPile = new CardPile(PileType.Play);
        Relics = new List<RelicModel>();
        PotionSlots = new PotionModel?[DefaultPotionSlotCount];
        Deck = new List<CardInstance>();
        OrbQueue = new OrbQueue();
        PetIds = new List<int>();
    }

    private PlayerState(PlayerState source)
        : base(source.CombatId, source.Side, source.CurrentHp, source.MaxHp)
    {
        CharacterId = source.CharacterId;
        BaseMaxEnergy = source.BaseMaxEnergy;
        Energy = source.Energy;
        Stars = source.Stars;
        TurnNumber = source.TurnNumber;
        Phase = source.Phase;
        IsActiveForHooks = source.IsActiveForHooks;
        DrawPile = source.DrawPile.CloneFor(this);
        Hand = source.Hand.CloneFor(this);
        DiscardPile = source.DiscardPile.CloneFor(this);
        ExhaustPile = source.ExhaustPile.CloneFor(this);
        PlayPile = source.PlayPile.CloneFor(this);
        Gold = source.Gold;
        BaseOrbSlotCount = source.BaseOrbSlotCount;
        CanUseOrRemovePotions = source.CanUseOrRemovePotions;
        var sourceRelics = source.Relics;
        Relics = new List<RelicModel>(sourceRelics.Count);
        for (int i = 0; i < sourceRelics.Count; i++)
        {
            Relics.Add(sourceRelics[i].CloneFor(this));
        }
        var sourceSlots = source.PotionSlots;
        PotionSlots = new PotionModel?[sourceSlots.Length];
        for (int i = 0; i < sourceSlots.Length; i++)
        {
            PotionSlots[i] = sourceSlots[i]?.CloneFor(this);
        }
        var sourceDeck = source.Deck;
        Deck = new List<CardInstance>(sourceDeck.Count);
        for (int i = 0; i < sourceDeck.Count; i++)
        {
            Deck.Add(sourceDeck[i].CloneFor(this));
        }
        OrbQueue = source.OrbQueue.CloneFor(this);
        PetIds = new List<int>(source.PetIds);
    }

    public override bool IsPlayer => true;

    public string CharacterId { get; }

    public int BaseMaxEnergy { get; set; }

    public int Energy { get; set; }

    public int Stars { get; set; }

    public int TurnNumber { get; set; }

    public PlayerTurnPhase Phase { get; set; }

    public bool IsActiveForHooks { get; private set; }

    public CardPile DrawPile { get; }

    public CardPile Hand { get; }

    public CardPile DiscardPile { get; }

    public CardPile ExhaustPile { get; }

    public CardPile PlayPile { get; }

    public IReadOnlyList<CardPile> AllPiles => _allPiles ??= new[] { Hand, DrawPile, DiscardPile, ExhaustPile, PlayPile };

    public int Gold { get; set; }

    public List<RelicModel> Relics { get; }

    public PotionModel?[] PotionSlots { get; set; }

    public int MaxPotionCount => PotionSlots.Length;

    public bool HasOpenPotionSlot
    {
        get
        {
            var slots = PotionSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is null)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public bool CanUseOrRemovePotions { get; set; } = true;

    public int BaseOrbSlotCount { get; set; }

    public List<CardInstance> Deck { get; }

    public OrbQueue OrbQueue { get; }

    public List<int> PetIds { get; }

    public RelicModel? GetRelic(string relicId)
    {
        var relics = Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i].Id == relicId)
            {
                return relics[i];
            }
        }
        return null;
    }

    public bool HasRelic(string relicId) => GetRelic(relicId) != null;

    public void AddToMaxPotionCount(int maxPotionCountIncrease) => SetMaxPotionCountInternal(PotionSlots.Length + maxPotionCountIncrease);

    public void SubtractFromMaxPotionCount(int maxPotionCountDecrease) => SetMaxPotionCountInternal(PotionSlots.Length - maxPotionCountDecrease);

    private void SetMaxPotionCountInternal(int newMaxPotionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newMaxPotionCount);
        var slots = PotionSlots;
        if (newMaxPotionCount == slots.Length)
        {
            return;
        }
        if (newMaxPotionCount < slots.Length)
        {
            for (int i = slots.Length - 1; i >= newMaxPotionCount; i--)
            {
                var potion = slots[i];
                if (potion is null)
                {
                    continue;
                }
                int open = Array.IndexOf(slots, null, 0, i);
                if (open < 0)
                {
                    throw new InvalidOperationException($"Cannot remove potion slot {i}: every slot is full, and the game indexes slot -1 here (Entities.Players/Player.cs:1004-1007).");
                }
                if (open < newMaxPotionCount)
                {
                    slots[open] = potion;
                }
            }
        }
        var resized = new PotionModel?[newMaxPotionCount];
        Array.Copy(slots, resized, Math.Min(slots.Length, newMaxPotionCount));
        PotionSlots = resized;
    }

    public CardPile GetPile(PileType type) => type switch
    {
        PileType.Draw => DrawPile,
        PileType.Hand => Hand,
        PileType.Discard => DiscardPile,
        PileType.Exhaust => ExhaustPile,
        PileType.Play => PlayPile,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No combat pile of this type")
    };

    public CardPile? FindPileContaining(CardInstance card)
    {
        var piles = AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            if (piles[i].Contains(card))
            {
                return piles[i];
            }
        }
        return null;
    }

    public void LoseStars(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("Must not be negative.", nameof(amount));
        }
        Stars = (int)Math.Max(Stars - amount, 0m);
    }

    public void GainStars(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("Must not be negative.", nameof(amount));
        }
        Stars = (int)Math.Max(Stars + amount, 0m);
    }

    public void IncrementTurnNumber()
    {
        TurnNumber++;
    }

    public void EndOfTurnCleanup()
    {
        var piles = AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].EndOfTurnCleanup();
            }
        }
    }

    public void ActivateHooks()
    {
        IsActiveForHooks = true;
    }

    public void DeactivateHooks()
    {
        IsActiveForHooks = false;
    }

    public PlayerState DeepClone()
    {
        var clone = new PlayerState(this);
        CopyCreatureFieldsInto(clone);
        return clone;
    }

    public override CreatureState DeepCloneCreature() => DeepClone();

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.Player);
        WriteCreatureCanonical(w);
        w.WriteString(CharacterId);
        w.WriteInt32(BaseMaxEnergy);
        w.WriteInt32(Energy);
        w.WriteInt32(Stars);
        w.WriteInt32(TurnNumber);
        w.WriteInt32((int)Phase);
        w.WriteBool(IsActiveForHooks);
        Hand.WriteCanonical(w);
        DrawPile.WriteCanonical(w);
        DiscardPile.WriteCanonical(w);
        ExhaustPile.WriteCanonical(w);
        PlayPile.WriteCanonical(w);
        w.WriteTag(StateCanonicalTags.PlayerPersistent);
        w.WriteInt32(Gold);
        w.WriteInt32(BaseOrbSlotCount);
        w.WriteBool(CanUseOrRemovePotions);
        var relics = Relics;
        w.WriteTag(StateCanonicalTags.RelicList);
        w.WriteCount(relics.Count);
        for (int i = 0; i < relics.Count; i++)
        {
            relics[i].WriteCanonical(w);
        }
        var slots = PotionSlots;
        w.WriteTag(StateCanonicalTags.PotionSlots);
        w.WriteCount(slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            var potion = slots[i];
            w.WriteBool(potion is not null);
            potion?.WriteCanonical(w);
        }
        var deck = Deck;
        w.WriteTag(StateCanonicalTags.Deck);
        w.WriteCount(deck.Count);
        for (int i = 0; i < deck.Count; i++)
        {
            deck[i].WriteCanonical(w);
        }
        w.WriteTag(StateCanonicalTags.PlayerCombatExtras);
        OrbQueue.WriteCanonical(w);
        var pets = PetIds;
        w.WriteCount(pets.Count);
        for (int i = 0; i < pets.Count; i++)
        {
            w.WriteInt32(pets[i]);
        }
    }
}

public sealed class MonsterState : CreatureState
{
    private const string StunnedMoveId = "STUNNED";

    private MonsterAiRuntime? _ai;

    public MonsterState(int combatId, MonsterDef def, int currentHp, int maxHp, MonsterExtraState? extra, string? slotName)
        : base(combatId, CombatSide.Enemy, currentHp, maxHp)
    {
        Def = def;
        Extra = extra;
        SlotName = slotName;
    }

    private MonsterState(MonsterState source)
        : base(source.CombatId, source.Side, source.CurrentHp, source.MaxHp)
    {
        Def = source.Def;
        Extra = source.Extra?.DeepClone();
        SlotName = source.SlotName;
        MaxHpBeforeModification = source.MaxHpBeforeModification;
        SpawnedThisTurn = source.SpawnedThisTurn;
        PetOwnerId = source.PetOwnerId;
        Listener = source.Listener is IOwnedListener<MonsterState> owned ? owned.CloneFor(this) : source.Listener;
        _ai = source._ai?.DeepClone();
    }

    public override bool IsPlayer => false;

    public MonsterDef Def { get; }

    public string MonsterId => Def.Id;

    public MonsterAiRuntime Ai => _ai ?? throw new InvalidOperationException($"Monster {Def.Id} has no AI runtime yet");

    public bool HasAi => _ai != null;

    public void InitAi(MonsterAiRuntime ai)
    {
        ArgumentNullException.ThrowIfNull(ai);
        if (_ai != null)
        {
            throw new InvalidOperationException($"Monster {Def.Id} AI runtime already set");
        }
        _ai = ai;
    }

    public MonsterExtraState? Extra { get; }

    public string? SlotName { get; }

    public int? MaxHpBeforeModification { get; set; }

    public bool SpawnedThisTurn { get; set; }

    public int PetOwnerId { get; set; } = PowerModel.NoCreature;

    public AbstractModel? Listener { get; set; }

    public bool IsStunned => _ai?.NextMove?.Id == StunnedMoveId;

    public MonsterState DeepClone()
    {
        var clone = new MonsterState(this);
        CopyCreatureFieldsInto(clone);
        return clone;
    }

    public override CreatureState DeepCloneCreature() => DeepClone();

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(StateCanonicalTags.Monster);
        WriteCreatureCanonical(w);
        w.WriteString(Def.Id);
        w.WriteString(SlotName);
        w.WriteNullableInt32(MaxHpBeforeModification);
        w.WriteBool(SpawnedThisTurn);
        var extra = Extra;
        w.WriteBool(extra != null);
        extra?.WriteCanonical(w);
        var ai = _ai;
        w.WriteBool(ai != null);
        ai?.WriteCanonical(w);
        w.WriteTag(StateCanonicalTags.MonsterKernel);
        w.WriteInt32(PetOwnerId);
        w.WriteBool(Listener != null);
    }
}
