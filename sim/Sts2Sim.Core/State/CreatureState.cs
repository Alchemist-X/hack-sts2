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
        DrawPile = source.DrawPile.DeepClone();
        Hand = source.Hand.DeepClone();
        DiscardPile = source.DiscardPile.DeepClone();
        ExhaustPile = source.ExhaustPile.DeepClone();
        PlayPile = source.PlayPile.DeepClone();
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
    }
}
