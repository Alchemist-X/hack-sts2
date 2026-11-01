namespace Sts2Sim.Core.State;

public interface IHistoryEntry
{
    HistoryStamp Stamp { get; }
}

public readonly record struct HistoryStamp(int Sequence, int ActorId, int RoundNumber, CombatSide CurrentSide, int PlayerTurnNumber)
{
    public bool HappenedThisTurn(CombatState? cs)
        => throw new NotImplementedException("HistoryStamp.HappenedThisTurn: state agent (Combat.History/CombatHistoryEntry.cs:67-113)");

    public bool HappenedLastPlayerTurn(PlayerState player)
        => throw new NotImplementedException("HistoryStamp.HappenedLastPlayerTurn: state agent (Combat.History/CombatHistoryEntry.cs:115-125)");

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.Stamp);
        w.WriteInt32(Sequence);
        w.WriteInt32(ActorId);
        w.WriteInt32(RoundNumber);
        w.WriteInt32((int)CurrentSide);
        w.WriteInt32(PlayerTurnNumber);
    }
}

public readonly record struct HistoryCardPlay(int PlayId, int CardInstanceId, string CardId, int PlayerId, int TargetId, PileType ResultPile, ResourceInfo Resources, bool IsAutoPlay, int PlayIndex, int PlayCount)
{
    public bool IsFirstInSeries => PlayIndex == 0;

    public bool IsLastInSeries => PlayIndex == PlayCount - 1;

    public static HistoryCardPlay From(CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay);
        var card = cardPlay.Card;
        return new HistoryCardPlay(
            cardPlay.PlayId,
            card.InstanceId,
            card.Id,
            card.HasOwner ? card.Owner.CombatId : CombatHistory.NoId,
            cardPlay.Target?.CombatId ?? CombatHistory.NoId,
            cardPlay.ResultPile,
            cardPlay.Resources,
            cardPlay.IsAutoPlay,
            cardPlay.PlayIndex,
            cardPlay.PlayCount);
    }

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardPlay);
        w.WriteInt32(PlayId);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteInt32(PlayerId);
        w.WriteInt32(TargetId);
        w.WriteInt32((int)ResultPile);
        w.WriteInt32(Resources.EnergySpent);
        w.WriteInt32(Resources.EnergyValue);
        w.WriteInt32(Resources.StarsSpent);
        w.WriteInt32(Resources.StarValue);
        w.WriteBool(IsAutoPlay);
        w.WriteInt32(PlayIndex);
        w.WriteInt32(PlayCount);
    }
}

public readonly record struct HistoryDamageResult(int ReceiverId, ValueProp Props, int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasBlockBroken, bool WasFullyBlocked, bool WasTargetKilled)
{
    public int TotalDamage => BlockedDamage + UnblockedDamage;

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.DamageResult);
        w.WriteInt32(ReceiverId);
        w.WriteInt32((int)Props);
        w.WriteInt32(BlockedDamage);
        w.WriteInt32(UnblockedDamage);
        w.WriteInt32(OverkillDamage);
        w.WriteBool(WasBlockBroken);
        w.WriteBool(WasFullyBlocked);
        w.WriteBool(WasTargetKilled);
    }
}

public readonly record struct BlockGainedEntry(HistoryStamp Stamp, int Amount, ValueProp Props, HistoryCardPlay? CardPlay) : IHistoryEntry
{
    public int ReceiverId => Stamp.ActorId;

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.BlockGained);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
        w.WriteInt32((int)Props);
        w.WriteBool(CardPlay.HasValue);
        if (CardPlay is { } cardPlay)
        {
            cardPlay.WriteCanonical(w);
        }
    }
}

public readonly record struct CardAfflictedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, string AfflictionId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardAfflicted);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteString(AfflictionId);
    }
}

public readonly record struct CardDiscardedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardDiscarded);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
    }
}

public readonly record struct CardDrawnEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, bool FromHandDraw) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardDrawn);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteBool(FromHandDraw);
    }
}

public readonly record struct CardExhaustedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardExhausted);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
    }
}

public readonly record struct CardGeneratedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, int CreatorId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardGenerated);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteInt32(CreatorId);
    }
}

public readonly record struct CardPlayFinishedEntry(HistoryStamp Stamp, HistoryCardPlay CardPlay, bool WasEthereal) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardPlayFinished);
        Stamp.WriteCanonical(w);
        CardPlay.WriteCanonical(w);
        w.WriteBool(WasEthereal);
    }
}

public readonly record struct CardPlayStartedEntry(HistoryStamp Stamp, HistoryCardPlay CardPlay) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardPlayStarted);
        Stamp.WriteCanonical(w);
        CardPlay.WriteCanonical(w);
    }
}

public readonly record struct CreatureAttackedEntry(HistoryStamp Stamp, IReadOnlyList<HistoryDamageResult> DamageResults) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CreatureAttacked);
        Stamp.WriteCanonical(w);
        w.WriteCount(DamageResults.Count);
        for (int i = 0; i < DamageResults.Count; i++)
        {
            DamageResults[i].WriteCanonical(w);
        }
    }
}

public readonly record struct DamageReceivedEntry(HistoryStamp Stamp, HistoryDamageResult Result, int DealerId, int CardSourceInstanceId, string? CardSourceId) : IHistoryEntry
{
    public int ReceiverId => Stamp.ActorId;

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.DamageReceived);
        Stamp.WriteCanonical(w);
        Result.WriteCanonical(w);
        w.WriteInt32(DealerId);
        w.WriteInt32(CardSourceInstanceId);
        w.WriteString(CardSourceId);
    }
}

public readonly record struct EnergySpentEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.EnergySpent);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public readonly record struct MonsterPerformedMoveEntry(HistoryStamp Stamp, string MonsterId, string MoveId, IReadOnlyList<int>? TargetIds) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.MonsterPerformedMove);
        Stamp.WriteCanonical(w);
        w.WriteString(MonsterId);
        w.WriteString(MoveId);
        w.WriteBool(TargetIds != null);
        if (TargetIds is { } ids)
        {
            w.WriteCount(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                w.WriteInt32(ids[i]);
            }
        }
    }
}

public readonly record struct OrbChanneledEntry(HistoryStamp Stamp, string OrbId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.OrbChanneled);
        Stamp.WriteCanonical(w);
        w.WriteString(OrbId);
    }
}

public readonly record struct PotionUsedEntry(HistoryStamp Stamp, string PotionId, int TargetId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.PotionUsed);
        Stamp.WriteCanonical(w);
        w.WriteString(PotionId);
        w.WriteInt32(TargetId);
    }
}

public readonly record struct PowerReceivedEntry(HistoryStamp Stamp, string PowerId, decimal Amount, int ApplierId) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.PowerReceived);
        Stamp.WriteCanonical(w);
        w.WriteString(PowerId);
        w.WriteDecimal(Amount);
        w.WriteInt32(ApplierId);
    }
}

public readonly record struct StarsModifiedEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.StarsModified);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public readonly record struct SummonedEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry
{
    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.Summoned);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public sealed class CombatHistory : ICanonical
{
    public const int NoId = -1;

    private List<BlockGainedEntry>? _blockGained;
    private List<CardAfflictedEntry>? _cardAfflicted;
    private List<CardDiscardedEntry>? _cardDiscarded;
    private List<CardDrawnEntry>? _cardDrawn;
    private List<CardExhaustedEntry>? _cardExhausted;
    private List<CardGeneratedEntry>? _cardGenerated;
    private List<CardPlayFinishedEntry>? _cardPlayFinished;
    private List<CardPlayStartedEntry>? _cardPlayStarted;
    private List<CreatureAttackedEntry>? _creatureAttacked;
    private List<DamageReceivedEntry>? _damageReceived;
    private List<EnergySpentEntry>? _energySpent;
    private List<MonsterPerformedMoveEntry>? _monsterPerformedMove;
    private List<OrbChanneledEntry>? _orbChanneled;
    private List<PotionUsedEntry>? _potionUsed;
    private List<PowerReceivedEntry>? _powerReceived;
    private List<StarsModifiedEntry>? _starsModified;
    private List<SummonedEntry>? _summoned;
    private int _count;

    public int Count => _count;

    public IReadOnlyList<BlockGainedEntry> BlockGainedEntries => (IReadOnlyList<BlockGainedEntry>?)_blockGained ?? Array.Empty<BlockGainedEntry>();

    public IReadOnlyList<CardAfflictedEntry> CardAfflictedEntries => (IReadOnlyList<CardAfflictedEntry>?)_cardAfflicted ?? Array.Empty<CardAfflictedEntry>();

    public IReadOnlyList<CardDiscardedEntry> CardDiscardedEntries => (IReadOnlyList<CardDiscardedEntry>?)_cardDiscarded ?? Array.Empty<CardDiscardedEntry>();

    public IReadOnlyList<CardDrawnEntry> CardDrawnEntries => (IReadOnlyList<CardDrawnEntry>?)_cardDrawn ?? Array.Empty<CardDrawnEntry>();

    public IReadOnlyList<CardExhaustedEntry> CardExhaustedEntries => (IReadOnlyList<CardExhaustedEntry>?)_cardExhausted ?? Array.Empty<CardExhaustedEntry>();

    public IReadOnlyList<CardGeneratedEntry> CardGeneratedEntries => (IReadOnlyList<CardGeneratedEntry>?)_cardGenerated ?? Array.Empty<CardGeneratedEntry>();

    public IReadOnlyList<CardPlayFinishedEntry> CardPlayFinishedEntries => (IReadOnlyList<CardPlayFinishedEntry>?)_cardPlayFinished ?? Array.Empty<CardPlayFinishedEntry>();

    public IReadOnlyList<CardPlayStartedEntry> CardPlayStartedEntries => (IReadOnlyList<CardPlayStartedEntry>?)_cardPlayStarted ?? Array.Empty<CardPlayStartedEntry>();

    public IReadOnlyList<CreatureAttackedEntry> CreatureAttackedEntries => (IReadOnlyList<CreatureAttackedEntry>?)_creatureAttacked ?? Array.Empty<CreatureAttackedEntry>();

    public IReadOnlyList<DamageReceivedEntry> DamageReceivedEntries => (IReadOnlyList<DamageReceivedEntry>?)_damageReceived ?? Array.Empty<DamageReceivedEntry>();

    public IReadOnlyList<EnergySpentEntry> EnergySpentEntries => (IReadOnlyList<EnergySpentEntry>?)_energySpent ?? Array.Empty<EnergySpentEntry>();

    public IReadOnlyList<MonsterPerformedMoveEntry> MonsterPerformedMoveEntries => (IReadOnlyList<MonsterPerformedMoveEntry>?)_monsterPerformedMove ?? Array.Empty<MonsterPerformedMoveEntry>();

    public IReadOnlyList<OrbChanneledEntry> OrbChanneledEntries => (IReadOnlyList<OrbChanneledEntry>?)_orbChanneled ?? Array.Empty<OrbChanneledEntry>();

    public IReadOnlyList<PotionUsedEntry> PotionUsedEntries => (IReadOnlyList<PotionUsedEntry>?)_potionUsed ?? Array.Empty<PotionUsedEntry>();

    public IReadOnlyList<PowerReceivedEntry> PowerReceivedEntries => (IReadOnlyList<PowerReceivedEntry>?)_powerReceived ?? Array.Empty<PowerReceivedEntry>();

    public IReadOnlyList<StarsModifiedEntry> StarsModifiedEntries => (IReadOnlyList<StarsModifiedEntry>?)_starsModified ?? Array.Empty<StarsModifiedEntry>();

    public IReadOnlyList<SummonedEntry> SummonedEntries => (IReadOnlyList<SummonedEntry>?)_summoned ?? Array.Empty<SummonedEntry>();

    public IReadOnlyList<CardPlayStartedEntry> CardPlaysStarted => CardPlayStartedEntries;

    public IReadOnlyList<CardPlayFinishedEntry> CardPlaysFinished => CardPlayFinishedEntries;

    public void AppendBlockGained(CombatState cs, CreatureState receiver, int amount, ValueProp props, CardPlay? cardPlay)
        => throw new NotImplementedException("CombatHistory.AppendBlockGained: state agent (Combat.History/CombatHistory.cs:119)");

    public void AppendCardAfflicted(CombatState cs, CardInstance card, AfflictionModel affliction)
        => throw new NotImplementedException("CombatHistory.AppendCardAfflicted: state agent (Combat.History/CombatHistory.cs:84)");

    public void AppendCardDiscarded(CombatState cs, CardInstance card)
        => throw new NotImplementedException("CombatHistory.AppendCardDiscarded: state agent (Combat.History/CombatHistory.cs:89)");

    public void AppendCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
        => throw new NotImplementedException("CombatHistory.AppendCardDrawn: state agent (Combat.History/CombatHistory.cs:94)");

    public void AppendCardExhausted(CombatState cs, CardInstance card)
        => throw new NotImplementedException("CombatHistory.AppendCardExhausted: state agent (Combat.History/CombatHistory.cs:99)");

    public void AppendCardGenerated(CombatState cs, CardInstance card, PlayerState? creator)
        => throw new NotImplementedException("CombatHistory.AppendCardGenerated: state agent (Combat.History/CombatHistory.cs:104)");

    public void AppendCardPlayFinished(CombatState cs, CardPlay cardPlay)
        => throw new NotImplementedException("CombatHistory.AppendCardPlayFinished: state agent (Combat.History/CombatHistory.cs:79)");

    public void AppendCardPlayStarted(CombatState cs, CardPlay cardPlay)
        => throw new NotImplementedException("CombatHistory.AppendCardPlayStarted: state agent (Combat.History/CombatHistory.cs:74)");

    public void AppendCreatureAttacked(CombatState cs, CreatureState attacker, IReadOnlyList<DamageResult> damageResults)
        => throw new NotImplementedException("CombatHistory.AppendCreatureAttacked: state agent (Combat.History/CombatHistory.cs:109)");

    public void AppendDamageReceived(CombatState cs, CreatureState receiver, CreatureState? dealer, DamageResult result, CardInstance? cardSource)
        => throw new NotImplementedException("CombatHistory.AppendDamageReceived: state agent (Combat.History/CombatHistory.cs:114)");

    public void AppendEnergySpent(CombatState cs, int amount, PlayerState player)
        => throw new NotImplementedException("CombatHistory.AppendEnergySpent: state agent (Combat.History/CombatHistory.cs:124)");

    public void AppendMonsterPerformedMove(CombatState cs, MonsterState monster, MoveStateDef move, IEnumerable<CreatureState>? targets)
        => throw new NotImplementedException("CombatHistory.AppendMonsterPerformedMove: state agent (Combat.History/CombatHistory.cs:129)");

    public void AppendOrbChanneled(CombatState cs, OrbModel orb)
        => throw new NotImplementedException("CombatHistory.AppendOrbChanneled: state agent (Combat.History/CombatHistory.cs:134)");

    public void AppendPotionUsed(CombatState cs, PotionModel potion, CreatureState? target)
        => throw new NotImplementedException("CombatHistory.AppendPotionUsed: state agent (Combat.History/CombatHistory.cs:139)");

    public void AppendPowerReceived(CombatState cs, PowerModel power, decimal amount, CreatureState? applier)
        => throw new NotImplementedException("CombatHistory.AppendPowerReceived: state agent (Combat.History/CombatHistory.cs:144)");

    public void AppendStarsModified(CombatState cs, int amount, PlayerState player)
        => throw new NotImplementedException("CombatHistory.AppendStarsModified: state agent (Combat.History/CombatHistory.cs:149)");

    public void AppendSummoned(CombatState cs, int amount, PlayerState player)
        => throw new NotImplementedException("CombatHistory.AppendSummoned: state agent (Combat.History/CombatHistory.cs:154)");

    public IReadOnlyList<T> Entries<T>() where T : struct, IHistoryEntry
    {
        if (typeof(T) == typeof(BlockGainedEntry))
        {
            return (IReadOnlyList<T>)BlockGainedEntries;
        }
        if (typeof(T) == typeof(CardAfflictedEntry))
        {
            return (IReadOnlyList<T>)CardAfflictedEntries;
        }
        if (typeof(T) == typeof(CardDiscardedEntry))
        {
            return (IReadOnlyList<T>)CardDiscardedEntries;
        }
        if (typeof(T) == typeof(CardDrawnEntry))
        {
            return (IReadOnlyList<T>)CardDrawnEntries;
        }
        if (typeof(T) == typeof(CardExhaustedEntry))
        {
            return (IReadOnlyList<T>)CardExhaustedEntries;
        }
        if (typeof(T) == typeof(CardGeneratedEntry))
        {
            return (IReadOnlyList<T>)CardGeneratedEntries;
        }
        if (typeof(T) == typeof(CardPlayFinishedEntry))
        {
            return (IReadOnlyList<T>)CardPlayFinishedEntries;
        }
        if (typeof(T) == typeof(CardPlayStartedEntry))
        {
            return (IReadOnlyList<T>)CardPlayStartedEntries;
        }
        if (typeof(T) == typeof(CreatureAttackedEntry))
        {
            return (IReadOnlyList<T>)CreatureAttackedEntries;
        }
        if (typeof(T) == typeof(DamageReceivedEntry))
        {
            return (IReadOnlyList<T>)DamageReceivedEntries;
        }
        if (typeof(T) == typeof(EnergySpentEntry))
        {
            return (IReadOnlyList<T>)EnergySpentEntries;
        }
        if (typeof(T) == typeof(MonsterPerformedMoveEntry))
        {
            return (IReadOnlyList<T>)MonsterPerformedMoveEntries;
        }
        if (typeof(T) == typeof(OrbChanneledEntry))
        {
            return (IReadOnlyList<T>)OrbChanneledEntries;
        }
        if (typeof(T) == typeof(PotionUsedEntry))
        {
            return (IReadOnlyList<T>)PotionUsedEntries;
        }
        if (typeof(T) == typeof(PowerReceivedEntry))
        {
            return (IReadOnlyList<T>)PowerReceivedEntries;
        }
        if (typeof(T) == typeof(StarsModifiedEntry))
        {
            return (IReadOnlyList<T>)StarsModifiedEntries;
        }
        if (typeof(T) == typeof(SummonedEntry))
        {
            return (IReadOnlyList<T>)SummonedEntries;
        }
        throw new ArgumentException($"{typeof(T).Name} is not a combat history entry type.", nameof(T));
    }

    public int CountThisTurn<T>(CombatState cs, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
        => throw new NotImplementedException("CombatHistory.CountThisTurn: state agent (Combat.History/CombatHistoryEntry.cs:67-113)");

    public int CountLastPlayerTurn<T>(PlayerState player, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
        => throw new NotImplementedException("CombatHistory.CountLastPlayerTurn: state agent (Combat.History/CombatHistoryEntry.cs:115-125)");

    public IEnumerable<T> ByActor<T>(int actorId) where T : struct, IHistoryEntry
        => throw new NotImplementedException("CombatHistory.ByActor: state agent (Combat.History/CombatHistoryEntry.cs:15-20)");

    public void Clear()
    {
        _blockGained?.Clear();
        _cardAfflicted?.Clear();
        _cardDiscarded?.Clear();
        _cardDrawn?.Clear();
        _cardExhausted?.Clear();
        _cardGenerated?.Clear();
        _cardPlayFinished?.Clear();
        _cardPlayStarted?.Clear();
        _creatureAttacked?.Clear();
        _damageReceived?.Clear();
        _energySpent?.Clear();
        _monsterPerformedMove?.Clear();
        _orbChanneled?.Clear();
        _potionUsed?.Clear();
        _powerReceived?.Clear();
        _starsModified?.Clear();
        _summoned?.Clear();
        _count = 0;
    }

    public CombatHistory DeepClone()
    {
        var clone = new CombatHistory { _count = _count };
        if (_blockGained is { Count: > 0 } blockGained)
        {
            clone._blockGained = new List<BlockGainedEntry>(blockGained);
        }
        if (_cardAfflicted is { Count: > 0 } cardAfflicted)
        {
            clone._cardAfflicted = new List<CardAfflictedEntry>(cardAfflicted);
        }
        if (_cardDiscarded is { Count: > 0 } cardDiscarded)
        {
            clone._cardDiscarded = new List<CardDiscardedEntry>(cardDiscarded);
        }
        if (_cardDrawn is { Count: > 0 } cardDrawn)
        {
            clone._cardDrawn = new List<CardDrawnEntry>(cardDrawn);
        }
        if (_cardExhausted is { Count: > 0 } cardExhausted)
        {
            clone._cardExhausted = new List<CardExhaustedEntry>(cardExhausted);
        }
        if (_cardGenerated is { Count: > 0 } cardGenerated)
        {
            clone._cardGenerated = new List<CardGeneratedEntry>(cardGenerated);
        }
        if (_cardPlayFinished is { Count: > 0 } cardPlayFinished)
        {
            clone._cardPlayFinished = new List<CardPlayFinishedEntry>(cardPlayFinished);
        }
        if (_cardPlayStarted is { Count: > 0 } cardPlayStarted)
        {
            clone._cardPlayStarted = new List<CardPlayStartedEntry>(cardPlayStarted);
        }
        if (_creatureAttacked is { Count: > 0 } creatureAttacked)
        {
            clone._creatureAttacked = new List<CreatureAttackedEntry>(creatureAttacked);
        }
        if (_damageReceived is { Count: > 0 } damageReceived)
        {
            clone._damageReceived = new List<DamageReceivedEntry>(damageReceived);
        }
        if (_energySpent is { Count: > 0 } energySpent)
        {
            clone._energySpent = new List<EnergySpentEntry>(energySpent);
        }
        if (_monsterPerformedMove is { Count: > 0 } monsterPerformedMove)
        {
            clone._monsterPerformedMove = new List<MonsterPerformedMoveEntry>(monsterPerformedMove);
        }
        if (_orbChanneled is { Count: > 0 } orbChanneled)
        {
            clone._orbChanneled = new List<OrbChanneledEntry>(orbChanneled);
        }
        if (_potionUsed is { Count: > 0 } potionUsed)
        {
            clone._potionUsed = new List<PotionUsedEntry>(potionUsed);
        }
        if (_powerReceived is { Count: > 0 } powerReceived)
        {
            clone._powerReceived = new List<PowerReceivedEntry>(powerReceived);
        }
        if (_starsModified is { Count: > 0 } starsModified)
        {
            clone._starsModified = new List<StarsModifiedEntry>(starsModified);
        }
        if (_summoned is { Count: > 0 } summoned)
        {
            clone._summoned = new List<SummonedEntry>(summoned);
        }
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.History);
        w.WriteInt32(_count);
        WriteList(w, _blockGained);
        WriteList(w, _cardAfflicted);
        WriteList(w, _cardDiscarded);
        WriteList(w, _cardDrawn);
        WriteList(w, _cardExhausted);
        WriteList(w, _cardGenerated);
        WriteList(w, _cardPlayFinished);
        WriteList(w, _cardPlayStarted);
        WriteList(w, _creatureAttacked);
        WriteList(w, _damageReceived);
        WriteList(w, _energySpent);
        WriteList(w, _monsterPerformedMove);
        WriteList(w, _orbChanneled);
        WriteList(w, _potionUsed);
        WriteList(w, _powerReceived);
        WriteList(w, _starsModified);
        WriteList(w, _summoned);
    }

    private static void WriteList(CanonicalWriter w, List<BlockGainedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardAfflictedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardDiscardedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardDrawnEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardExhaustedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardGeneratedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardPlayFinishedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CardPlayStartedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<CreatureAttackedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<DamageReceivedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<EnergySpentEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<MonsterPerformedMoveEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<OrbChanneledEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<PotionUsedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<PowerReceivedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<StarsModifiedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }

    private static void WriteList(CanonicalWriter w, List<SummonedEntry>? list)
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteCanonical(w);
        }
    }
}
