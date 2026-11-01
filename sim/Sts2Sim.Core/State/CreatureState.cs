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

    public const string OstyMonsterId = "OSTY";

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
        AscensionEffectsApplied = source.AscensionEffectsApplied;
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

    public bool AscensionEffectsApplied { get; set; }

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

    public T? GetRelic<T>() where T : class
    {
        var relics = Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i] is T t)
            {
                return t;
            }
        }
        return null;
    }

    public IEnumerable<PotionModel> Potions
    {
        get
        {
            var slots = PotionSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] is { } potion)
                {
                    yield return potion;
                }
            }
        }
    }

    public PotionModel? GetPotionAtSlotIndex(int index)
    {
        var slots = PotionSlots;
        if (index < 0 || index >= slots.Length)
        {
            throw new IndexOutOfRangeException($"Index {index} is not a valid potion slot index! Player has {slots.Length} potion slots");
        }
        return slots[index];
    }

    public void AddRelicInternal(RelicModel relic, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(relic);
        relic.AttachTo(this);
        if (index == -1)
        {
            Relics.Add(relic);
        }
        else
        {
            Relics.Insert(index, relic);
        }
    }

    public void RemoveRelicInternal(RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (!Relics.Contains(relic))
        {
            throw new InvalidOperationException($"Player does not have relic {relic.Id}");
        }
        Relics.Remove(relic);
        relic.RemoveInternal();
    }

    public void MeltRelicInternal(RelicModel relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (!relic.IsWax)
        {
            throw new InvalidOperationException($"{relic.Id} is not wax.");
        }
        if (relic.IsMelted)
        {
            throw new InvalidOperationException($"{relic.Id} is already melted.");
        }
        if (!Relics.Contains(relic))
        {
            throw new InvalidOperationException($"Player does not have relic {relic.Id}");
        }
        relic.IsMelted = true;
        relic.Status = RelicStatus.Disabled;
    }

    public int GetPotionSlotIndex(PotionModel potion) => Array.IndexOf(PotionSlots, potion);

    public PotionProcureResult AddPotionInternal(PotionModel potion, int slotIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(potion);
        var slots = PotionSlots;
        if (slotIndex < 0)
        {
            slotIndex = Array.IndexOf(slots, null);
        }
        if (slotIndex < 0)
        {
            return new PotionProcureResult(false, potion, PotionProcureFailureReason.TooFull);
        }
        if (slots[slotIndex] is not null)
        {
            return new PotionProcureResult(false, potion, PotionProcureFailureReason.TooFull);
        }
        potion.AttachTo(this);
        slots[slotIndex] = potion;
        return new PotionProcureResult(true, potion, PotionProcureFailureReason.None);
    }

    public void DiscardPotionInternal(PotionModel potion) => RemovePotionInternal(potion);

    public void RemoveUsedPotionInternal(PotionModel potion) => RemovePotionInternal(potion);

    private void RemovePotionInternal(PotionModel potion)
    {
        ArgumentNullException.ThrowIfNull(potion);
        int index = Array.IndexOf(PotionSlots, potion);
        if (index < 0)
        {
            throw new InvalidOperationException($"Tried to remove potion you don't have: {potion.Id}");
        }
        PotionSlots[index] = null;
    }

    public void AddToMaxPotionCount(int maxPotionCountIncrease) => SetMaxPotionCountInternal(PotionSlots.Length + maxPotionCountIncrease);

    public void SubtractFromMaxPotionCount(int maxPotionCountDecrease) => SetMaxPotionCountInternal(PotionSlots.Length - maxPotionCountDecrease);

    private void SetMaxPotionCountInternal(int newMaxPotionCount)
    {
        var slots = PotionSlots;
        if (newMaxPotionCount > slots.Length)
        {
            var grown = new PotionModel?[newMaxPotionCount];
            Array.Copy(slots, grown, slots.Length);
            PotionSlots = grown;
            return;
        }
        if (newMaxPotionCount >= slots.Length)
        {
            return;
        }
        var working = new List<PotionModel?>(slots);
        for (int num = working.Count - 1; num >= newMaxPotionCount; num--)
        {
            var potion = working[num];
            if (potion != null)
            {
                int num2 = working.IndexOf(null);
                if (num2 < newMaxPotionCount)
                {
                    working[num2] = potion;
                }
                else
                {
                    working[working.IndexOf(potion)] = null;
                }
            }
            working.RemoveAt(num);
        }
        PotionSlots = working.ToArray();
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

    public void AddPetInternal(MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        if (PetIds.Contains(pet.CombatId))
        {
            return;
        }
        if (pet.PetOwnerId != CombatId)
        {
            pet.SetPetOwner(this);
        }
        PetIds.Add(pet.CombatId);
    }

    public void OnPetDied(CombatState cs, MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(pet);
        if (!PetIds.Contains(pet.CombatId))
        {
            throw new InvalidOperationException($"Player does not have pet {pet.MonsterId} (combat id {pet.CombatId})");
        }
        if (Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet))
        {
            PetIds.Remove(pet.CombatId);
        }
    }

    public MonsterState? GetPet(CombatState cs, string monsterId)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var pets = PetIds;
        for (int i = 0; i < pets.Count; i++)
        {
            if (cs.GetCreatureById(pets[i]) is MonsterState pet && pet.MonsterId == monsterId)
            {
                return pet;
            }
        }
        return null;
    }

    public MonsterState? GetOsty(CombatState cs) => GetPet(cs, OstyMonsterId);

    public bool IsOstyAlive(CombatState cs) => GetOsty(cs)?.IsAlive ?? false;

    public bool IsOstyMissing(CombatState cs) => !IsOstyAlive(cs);

    public void ResetCombatState()
    {
        Hand.ClearInternal();
        DrawPile.ClearInternal();
        DiscardPile.ClearInternal();
        ExhaustPile.ClearInternal();
        PlayPile.ClearInternal();
        PetIds.Clear();
        TurnNumber = 1;
        Phase = PlayerTurnPhase.None;
        Energy = 0;
        Stars = 0;
        OrbQueue.Clear();
        OrbQueue.AddCapacity(BaseOrbSlotCount);
    }

    public void CombatStateAfterCombatEnd()
    {
        var piles = AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            piles[i].ClearInternal();
        }
        PetIds.Clear();
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
        w.WriteBool(AscensionEffectsApplied);
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
        : this(combatId, def, CombatSide.Enemy, currentHp, maxHp, extra, slotName)
    {
    }

    public MonsterState(int combatId, MonsterDef def, CombatSide side, int currentHp, int maxHp, MonsterExtraState? extra, string? slotName)
        : base(combatId, side, currentHp, maxHp)
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

    public bool IsPet => PetOwnerId != PowerModel.NoCreature;

    public AbstractModel? Listener { get; set; }

    public void SetPetOwner(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (PetOwnerId != PowerModel.NoCreature)
        {
            throw new InvalidOperationException($"Pet {MonsterId} (combat id {CombatId}) already has an owner.");
        }
        PetOwnerId = owner.CombatId;
    }

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
        ListenerCanonical.Write(w, Listener);
    }
}
