using System.Collections;

namespace Sts2Sim.Core.State;

public interface IHistoryEntry
{
    HistoryStamp Stamp { get; }
}

internal interface IHistoryEntryCanonical
{
    void WriteEntryCanonical(CanonicalWriter w);
}

public readonly record struct HistoryStamp(int Sequence, int ActorId, int RoundNumber, CombatSide CurrentSide, int PlayerTurnNumber)
{
    public bool HappenedThisTurn(CombatState? cs)
    {
        if (cs is null)
        {
            return false;
        }
        if (RoundNumber != cs.RoundNumber)
        {
            return false;
        }
        if (CurrentSide != cs.CurrentSide)
        {
            return false;
        }
        return cs.Player.TurnNumber == PlayerTurnNumber;
    }

    public bool HappenedLastPlayerTurn(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return PlayerTurnNumber == player.TurnNumber - 1;
    }

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

public readonly struct HistoryList<T> : IReadOnlyList<T>, IEquatable<HistoryList<T>> where T : struct, IEquatable<T>
{
    private readonly List<T>? _store;
    private readonly int _start;
    private readonly int _count;

    internal HistoryList(List<T> store, int start, int count)
    {
        _store = store;
        _start = start;
        _count = count;
    }

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The list holds {_count} items");
            }
            return _store![_start + index];
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public T[] ToArray()
    {
        if (_count == 0)
        {
            return Array.Empty<T>();
        }
        var copy = new T[_count];
        _store!.CopyTo(_start, copy, 0, _count);
        return copy;
    }

    public bool Equals(HistoryList<T> other)
    {
        if (_count != other._count)
        {
            return false;
        }
        for (int i = 0; i < _count; i++)
        {
            if (!this[i].Equals(other[i]))
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is HistoryList<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = _count;
        for (int i = 0; i < _count; i++)
        {
            hash = unchecked(hash * 31 + this[i].GetHashCode());
        }
        return hash;
    }

    public static bool operator ==(HistoryList<T> left, HistoryList<T> right) => left.Equals(right);

    public static bool operator !=(HistoryList<T> left, HistoryList<T> right) => !left.Equals(right);

    public struct Enumerator : IEnumerator<T>
    {
        private readonly HistoryList<T> _list;
        private int _index;

        internal Enumerator(HistoryList<T> list)
        {
            _list = list;
            _index = -1;
        }

        public readonly T Current => _list[_index];

        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _list._count;

        public void Reset() => _index = -1;

        public readonly void Dispose()
        {
        }
    }
}

public readonly record struct HistoryCardPlay(int PlayId, int CardInstanceId, string CardId, int PlayerId, int TargetId, PileType ResultPile, ResourceInfo Resources, bool IsAutoPlay, int PlayIndex, int PlayCount, CardType CardType = CardType.None, bool IsDupe = false)
{
    public bool IsFirstInSeries => PlayIndex == 0;

    public bool IsLastInSeries => PlayIndex == PlayCount - 1;

    public bool Equals(HistoryCardPlay other) => PlayId == other.PlayId;

    public override int GetHashCode() => PlayId;

    public bool IsSamePlay(CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay);
        return PlayId == cardPlay.PlayId;
    }

    public bool IsPlayOf(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return CardInstanceId == card.InstanceId;
    }

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
            cardPlay.PlayCount,
            card.Type,
            card.IsDupe);
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
        w.WriteInt32((int)CardType);
        w.WriteBool(IsDupe);
    }
}

public readonly record struct HistoryDamageResult(int ReceiverId, ValueProp Props, int BlockedDamage, int UnblockedDamage, int OverkillDamage, bool WasBlockBroken, bool WasFullyBlocked, bool WasTargetKilled)
{
    public int TotalDamage => BlockedDamage + UnblockedDamage;

    public static HistoryDamageResult From(DamageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new HistoryDamageResult(result.Receiver.CombatId, result.Props, result.BlockedDamage, result.UnblockedDamage, result.OverkillDamage, result.WasBlockBroken, result.WasFullyBlocked, result.WasTargetKilled);
    }

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

public readonly record struct BlockGainedEntry(HistoryStamp Stamp, int Amount, ValueProp Props, HistoryCardPlay? CardPlay) : IHistoryEntry, IHistoryEntryCanonical
{
    public int ReceiverId => Stamp.ActorId;

    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
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

public readonly record struct CardAfflictedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, string AfflictionId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardAfflicted);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteString(AfflictionId);
    }
}

public readonly record struct CardDiscardedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardDiscarded);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
    }
}

public readonly record struct CardDrawnEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, bool FromHandDraw, CardType CardType = CardType.None) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardDrawn);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteBool(FromHandDraw);
        w.WriteInt32((int)CardType);
    }
}

public readonly record struct CardExhaustedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardExhausted);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
    }
}

public readonly record struct CardGeneratedEntry(HistoryStamp Stamp, int CardInstanceId, string CardId, int CreatorId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardGenerated);
        Stamp.WriteCanonical(w);
        w.WriteInt32(CardInstanceId);
        w.WriteString(CardId);
        w.WriteInt32(CreatorId);
    }
}

public readonly record struct CardPlayFinishedEntry(HistoryStamp Stamp, HistoryCardPlay CardPlay, bool WasEthereal) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardPlayFinished);
        Stamp.WriteCanonical(w);
        CardPlay.WriteCanonical(w);
        w.WriteBool(WasEthereal);
    }
}

public readonly record struct CardPlayStartedEntry(HistoryStamp Stamp, HistoryCardPlay CardPlay) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CardPlayStarted);
        Stamp.WriteCanonical(w);
        CardPlay.WriteCanonical(w);
    }
}

public readonly record struct CreatureAttackedEntry(HistoryStamp Stamp, HistoryList<HistoryDamageResult> DamageResultList) : IHistoryEntry, IHistoryEntryCanonical
{
    public IReadOnlyList<HistoryDamageResult> DamageResults => DamageResultList;

    public int AttackerId => Stamp.ActorId;

    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.CreatureAttacked);
        Stamp.WriteCanonical(w);
        var results = DamageResultList;
        w.WriteCount(results.Count);
        for (int i = 0; i < results.Count; i++)
        {
            results[i].WriteCanonical(w);
        }
    }
}

public readonly record struct DamageReceivedEntry(HistoryStamp Stamp, HistoryDamageResult Result, int DealerId, int CardSourceInstanceId, string? CardSourceId, CombatSide DealerSide = CombatSide.None) : IHistoryEntry, IHistoryEntryCanonical
{
    public int ReceiverId => Stamp.ActorId;

    public bool HasDealer => DealerId != CombatHistory.NoId;

    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.DamageReceived);
        Stamp.WriteCanonical(w);
        Result.WriteCanonical(w);
        w.WriteInt32(DealerId);
        w.WriteInt32(CardSourceInstanceId);
        w.WriteString(CardSourceId);
        w.WriteInt32((int)DealerSide);
    }
}

public readonly record struct EnergySpentEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.EnergySpent);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public readonly record struct MonsterPerformedMoveEntry(HistoryStamp Stamp, string MonsterId, string MoveId, HistoryList<int>? TargetIdList) : IHistoryEntry, IHistoryEntryCanonical
{
    public IReadOnlyList<int>? TargetIds => TargetIdList;

    public int MonsterCombatId => Stamp.ActorId;

    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.MonsterPerformedMove);
        Stamp.WriteCanonical(w);
        w.WriteString(MonsterId);
        w.WriteString(MoveId);
        w.WriteBool(TargetIdList.HasValue);
        if (TargetIdList is { } ids)
        {
            w.WriteCount(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                w.WriteInt32(ids[i]);
            }
        }
    }
}

public readonly record struct OrbChanneledEntry(HistoryStamp Stamp, string OrbId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.OrbChanneled);
        Stamp.WriteCanonical(w);
        w.WriteString(OrbId);
    }
}

public readonly record struct PotionUsedEntry(HistoryStamp Stamp, string PotionId, int TargetId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.PotionUsed);
        Stamp.WriteCanonical(w);
        w.WriteString(PotionId);
        w.WriteInt32(TargetId);
    }
}

public readonly record struct PowerReceivedEntry(HistoryStamp Stamp, string PowerId, decimal Amount, int ApplierId) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.PowerReceived);
        Stamp.WriteCanonical(w);
        w.WriteString(PowerId);
        w.WriteDecimal(Amount);
        w.WriteInt32(ApplierId);
    }
}

public readonly record struct StarsModifiedEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.StarsModified);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public readonly record struct SummonedEntry(HistoryStamp Stamp, int Amount) : IHistoryEntry, IHistoryEntryCanonical
{
    void IHistoryEntryCanonical.WriteEntryCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.Summoned);
        Stamp.WriteCanonical(w);
        w.WriteInt32(Amount);
    }
}

public static class HistoryEntryExtensions
{
    public static bool HappenedThisTurn<T>(this T entry, CombatState? cs) where T : struct, IHistoryEntry => entry.Stamp.HappenedThisTurn(cs);

    public static bool HappenedLastPlayerTurn<T>(this T entry, PlayerState player) where T : struct, IHistoryEntry => entry.Stamp.HappenedLastPlayerTurn(player);

    public static bool IsByActor<T>(this T entry, CreatureState? actor) where T : struct, IHistoryEntry
        => actor is not null && entry.Stamp.ActorId == actor.CombatId;
}

public sealed class CombatHistory : ICanonical
{
    public const int NoId = -1;

    private const int EntryTypeCount = 17;
    private const int BlockGainedIndex = 0;
    private const int CardAfflictedIndex = 1;
    private const int CardDiscardedIndex = 2;
    private const int CardDrawnIndex = 3;
    private const int CardExhaustedIndex = 4;
    private const int CardGeneratedIndex = 5;
    private const int CardPlayFinishedIndex = 6;
    private const int CardPlayStartedIndex = 7;
    private const int CreatureAttackedIndex = 8;
    private const int DamageReceivedIndex = 9;
    private const int EnergySpentIndex = 10;
    private const int MonsterPerformedMoveIndex = 11;
    private const int OrbChanneledIndex = 12;
    private const int PotionUsedIndex = 13;
    private const int PowerReceivedIndex = 14;
    private const int StarsModifiedIndex = 15;
    private const int SummonedIndex = 16;

    private List<BlockGainedEntry>? _blockGained;
    private List<CardAfflictedEntry>? _cardAfflicted;
    private List<CardDiscardedEntry>? _cardDiscarded;
    private List<CardDrawnEntry>? _cardDrawn;
    private List<CardExhaustedEntry>? _cardExhausted;
    private List<CardGeneratedEntry>? _cardGenerated;
    private List<CardPlayFinishedEntry>? _cardPlayFinished;
    private List<CardPlayStartedEntry>? _cardPlayStarted;
    private List<CreatureAttackedRecord>? _creatureAttacked;
    private List<DamageReceivedEntry>? _damageReceived;
    private List<EnergySpentEntry>? _energySpent;
    private List<MonsterPerformedMoveRecord>? _monsterPerformedMove;
    private List<OrbChanneledEntry>? _orbChanneled;
    private List<PotionUsedEntry>? _potionUsed;
    private List<PowerReceivedEntry>? _powerReceived;
    private List<StarsModifiedEntry>? _starsModified;
    private List<SummonedEntry>? _summoned;
    private List<HistoryDamageResult>? _damageResultStore;
    private List<int>? _targetIdStore;
    private DigestState[]? _digests;
    private CreatureAttackedView? _creatureAttackedView;
    private MonsterPerformedMoveView? _monsterPerformedMoveView;
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

    public IReadOnlyList<CreatureAttackedEntry> CreatureAttackedEntries => _creatureAttacked is null ? Array.Empty<CreatureAttackedEntry>() : _creatureAttackedView ??= new CreatureAttackedView(this);

    public IReadOnlyList<DamageReceivedEntry> DamageReceivedEntries => (IReadOnlyList<DamageReceivedEntry>?)_damageReceived ?? Array.Empty<DamageReceivedEntry>();

    public IReadOnlyList<EnergySpentEntry> EnergySpentEntries => (IReadOnlyList<EnergySpentEntry>?)_energySpent ?? Array.Empty<EnergySpentEntry>();

    public IReadOnlyList<MonsterPerformedMoveEntry> MonsterPerformedMoveEntries => _monsterPerformedMove is null ? Array.Empty<MonsterPerformedMoveEntry>() : _monsterPerformedMoveView ??= new MonsterPerformedMoveView(this);

    public IReadOnlyList<OrbChanneledEntry> OrbChanneledEntries => (IReadOnlyList<OrbChanneledEntry>?)_orbChanneled ?? Array.Empty<OrbChanneledEntry>();

    public IReadOnlyList<PotionUsedEntry> PotionUsedEntries => (IReadOnlyList<PotionUsedEntry>?)_potionUsed ?? Array.Empty<PotionUsedEntry>();

    public IReadOnlyList<PowerReceivedEntry> PowerReceivedEntries => (IReadOnlyList<PowerReceivedEntry>?)_powerReceived ?? Array.Empty<PowerReceivedEntry>();

    public IReadOnlyList<StarsModifiedEntry> StarsModifiedEntries => (IReadOnlyList<StarsModifiedEntry>?)_starsModified ?? Array.Empty<StarsModifiedEntry>();

    public IReadOnlyList<SummonedEntry> SummonedEntries => (IReadOnlyList<SummonedEntry>?)_summoned ?? Array.Empty<SummonedEntry>();

    public IReadOnlyList<CardPlayStartedEntry> CardPlaysStarted => CardPlayStartedEntries;

    public IReadOnlyList<CardPlayFinishedEntry> CardPlaysFinished => CardPlayFinishedEntries;

    public void AppendBlockGained(CombatState cs, CreatureState receiver, int amount, ValueProp props, CardPlay? cardPlay)
    {
        int actor = receiver.CombatId;
        HistoryCardPlay? play = cardPlay == null ? null : HistoryCardPlay.From(cardPlay);
        Add(ref _blockGained, new BlockGainedEntry(NextStamp(cs, actor), amount, props, play));
    }

    public void AppendCardAfflicted(CombatState cs, CardInstance card, AfflictionModel affliction)
    {
        int actor = ActorOf(card);
        string afflictionId = affliction.Id;
        Add(ref _cardAfflicted, new CardAfflictedEntry(NextStamp(cs, actor), card.InstanceId, card.Id, afflictionId));
    }

    public void AppendCardDiscarded(CombatState cs, CardInstance card)
    {
        int actor = ActorOf(card);
        Add(ref _cardDiscarded, new CardDiscardedEntry(NextStamp(cs, actor), card.InstanceId, card.Id));
    }

    public void AppendCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        int actor = ActorOf(card);
        var type = card.Type;
        Add(ref _cardDrawn, new CardDrawnEntry(NextStamp(cs, actor), card.InstanceId, card.Id, fromHandDraw, type));
    }

    public void AppendCardExhausted(CombatState cs, CardInstance card)
    {
        int actor = ActorOf(card);
        Add(ref _cardExhausted, new CardExhaustedEntry(NextStamp(cs, actor), card.InstanceId, card.Id));
    }

    public void AppendCardGenerated(CombatState cs, CardInstance card, PlayerState? creator)
    {
        int actor = ActorOf(card);
        Add(ref _cardGenerated, new CardGeneratedEntry(NextStamp(cs, actor), card.InstanceId, card.Id, creator?.CombatId ?? NoId));
    }

    public void AppendCardPlayFinished(CombatState cs, CardPlay cardPlay)
    {
        var play = HistoryCardPlay.From(cardPlay);
        int actor = ActorOf(cardPlay.Card);
        bool wasEthereal = HasKeywordInCombat(cs, cardPlay.Card, CardKeyword.Ethereal);
        Add(ref _cardPlayFinished, new CardPlayFinishedEntry(NextStamp(cs, actor), play, wasEthereal));
    }

    public void AppendCardPlayStarted(CombatState cs, CardPlay cardPlay)
    {
        var play = HistoryCardPlay.From(cardPlay);
        int actor = ActorOf(cardPlay.Card);
        Add(ref _cardPlayStarted, new CardPlayStartedEntry(NextStamp(cs, actor), play));
    }

    public void AppendCreatureAttacked(CombatState cs, CreatureState attacker, IReadOnlyList<DamageResult> damageResults)
    {
        int actor = attacker.CombatId;
        var (start, count) = StoreDamageResults(damageResults);
        Add(ref _creatureAttacked, new CreatureAttackedRecord(NextStamp(cs, actor), start, count));
    }

    public void AppendDamageReceived(CombatState cs, CreatureState receiver, CreatureState? dealer, DamageResult result, CardInstance? cardSource)
    {
        int actor = receiver.CombatId;
        var damage = HistoryDamageResult.From(result);
        int dealerId = dealer?.CombatId ?? NoId;
        var dealerSide = dealer?.Side ?? CombatSide.None;
        Add(ref _damageReceived, new DamageReceivedEntry(NextStamp(cs, actor), damage, dealerId, cardSource?.InstanceId ?? NoId, cardSource?.Id, dealerSide));
    }

    public void AppendEnergySpent(CombatState cs, int amount, PlayerState player)
    {
        int actor = player.CombatId;
        Add(ref _energySpent, new EnergySpentEntry(NextStamp(cs, actor), amount));
    }

    public void AppendMonsterPerformedMove(CombatState cs, MonsterState monster, MoveStateDef move, IEnumerable<CreatureState>? targets)
    {
        int actor = monster.CombatId;
        string monsterId = monster.MonsterId;
        string moveId = move.Id;
        var (start, count) = StoreTargetIds(targets);
        Add(ref _monsterPerformedMove, new MonsterPerformedMoveRecord(NextStamp(cs, actor), monsterId, moveId, start, count));
    }

    public void AppendOrbChanneled(CombatState cs, OrbModel orb)
    {
        int actor = orb.HasOwner ? orb.Owner.CombatId : NoId;
        string orbId = orb.Id;
        Add(ref _orbChanneled, new OrbChanneledEntry(NextStamp(cs, actor), orbId));
    }

    public void AppendPotionUsed(CombatState cs, PotionModel potion, CreatureState? target)
    {
        int actor = potion.HasOwner ? potion.Owner.CombatId : NoId;
        string potionId = potion.Id;
        Add(ref _potionUsed, new PotionUsedEntry(NextStamp(cs, actor), potionId, target?.CombatId ?? NoId));
    }

    public void AppendPowerReceived(CombatState cs, PowerModel power, decimal amount, CreatureState? applier)
    {
        int actor = power.HasOwner ? power.Owner.CombatId : NoId;
        string powerId = power.Id;
        Add(ref _powerReceived, new PowerReceivedEntry(NextStamp(cs, actor), powerId, amount, applier?.CombatId ?? NoId));
    }

    public void AppendStarsModified(CombatState cs, int amount, PlayerState player)
    {
        int actor = player.CombatId;
        Add(ref _starsModified, new StarsModifiedEntry(NextStamp(cs, actor), amount));
    }

    public void AppendSummoned(CombatState cs, int amount, PlayerState player)
    {
        int actor = player.CombatId;
        Add(ref _summoned, new SummonedEntry(NextStamp(cs, actor), amount));
    }

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

    public int CountOf<T>(Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        if (predicate == null)
        {
            return entries.Count;
        }
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (predicate(entries[i]))
            {
                n++;
            }
        }
        return n;
    }

    public bool AnyOf<T>(Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        if (predicate == null)
        {
            return entries.Count > 0;
        }
        for (int i = 0; i < entries.Count; i++)
        {
            if (predicate(entries[i]))
            {
                return true;
            }
        }
        return false;
    }

    public int SumOf<T>(Func<T, int> selector, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        ArgumentNullException.ThrowIfNull(selector);
        var entries = Entries<T>();
        int sum = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (predicate == null || predicate(entry))
            {
                sum += selector(entry);
            }
        }
        return sum;
    }

    public T? LastOf<T>(Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (predicate == null || predicate(entry))
            {
                return entry;
            }
        }
        return null;
    }

    public int CountThisTurn<T>(CombatState cs, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.HappenedThisTurn(cs) && (predicate == null || predicate(entry)))
            {
                n++;
            }
        }
        return n;
    }

    public bool AnyThisTurn<T>(CombatState cs, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.HappenedThisTurn(cs) && (predicate == null || predicate(entry)))
            {
                return true;
            }
        }
        return false;
    }

    public int SumThisTurn<T>(CombatState cs, Func<T, int> selector, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        ArgumentNullException.ThrowIfNull(selector);
        var entries = Entries<T>();
        int sum = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.HappenedThisTurn(cs) && (predicate == null || predicate(entry)))
            {
                sum += selector(entry);
            }
        }
        return sum;
    }

    public int CountLastPlayerTurn<T>(PlayerState player, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        ArgumentNullException.ThrowIfNull(player);
        var entries = Entries<T>();
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.HappenedLastPlayerTurn(player) && (predicate == null || predicate(entry)))
            {
                n++;
            }
        }
        return n;
    }

    public bool AnyLastPlayerTurn<T>(PlayerState player, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        ArgumentNullException.ThrowIfNull(player);
        var entries = Entries<T>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.HappenedLastPlayerTurn(player) && (predicate == null || predicate(entry)))
            {
                return true;
            }
        }
        return false;
    }

    public IEnumerable<T> ByActor<T>(int actorId) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.ActorId == actorId)
            {
                yield return entry;
            }
        }
    }

    public int CountByActor<T>(int actorId, Func<T, bool>? predicate = null) where T : struct, IHistoryEntry
    {
        var entries = Entries<T>();
        int n = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Stamp.ActorId == actorId && (predicate == null || predicate(entry)))
            {
                n++;
            }
        }
        return n;
    }

    public void GetEntriesInOrder(List<IHistoryEntry> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        AddBoxed(buffer, _blockGained);
        AddBoxed(buffer, _cardAfflicted);
        AddBoxed(buffer, _cardDiscarded);
        AddBoxed(buffer, _cardDrawn);
        AddBoxed(buffer, _cardExhausted);
        AddBoxed(buffer, _cardGenerated);
        AddBoxed(buffer, _cardPlayFinished);
        AddBoxed(buffer, _cardPlayStarted);
        AddBoxed(buffer, _creatureAttacked is null ? null : CreatureAttackedEntries);
        AddBoxed(buffer, _damageReceived);
        AddBoxed(buffer, _energySpent);
        AddBoxed(buffer, _monsterPerformedMove is null ? null : MonsterPerformedMoveEntries);
        AddBoxed(buffer, _orbChanneled);
        AddBoxed(buffer, _potionUsed);
        AddBoxed(buffer, _powerReceived);
        AddBoxed(buffer, _starsModified);
        AddBoxed(buffer, _summoned);
        buffer.Sort(static (a, b) => a.Stamp.Sequence.CompareTo(b.Stamp.Sequence));
    }

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
        _damageResultStore = null;
        _targetIdStore = null;
        _digests = null;
        _count = 0;
    }

    public CombatHistory DeepClone()
    {
        var clone = new CombatHistory { _count = _count };
        if (_count == 0)
        {
            return clone;
        }
        clone._blockGained = CopyEntries(_blockGained);
        clone._cardAfflicted = CopyEntries(_cardAfflicted);
        clone._cardDiscarded = CopyEntries(_cardDiscarded);
        clone._cardDrawn = CopyEntries(_cardDrawn);
        clone._cardExhausted = CopyEntries(_cardExhausted);
        clone._cardGenerated = CopyEntries(_cardGenerated);
        clone._cardPlayFinished = CopyEntries(_cardPlayFinished);
        clone._cardPlayStarted = CopyEntries(_cardPlayStarted);
        clone._damageReceived = CopyEntries(_damageReceived);
        clone._energySpent = CopyEntries(_energySpent);
        clone._orbChanneled = CopyEntries(_orbChanneled);
        clone._potionUsed = CopyEntries(_potionUsed);
        clone._powerReceived = CopyEntries(_powerReceived);
        clone._starsModified = CopyEntries(_starsModified);
        clone._summoned = CopyEntries(_summoned);
        clone._creatureAttacked = CopyEntries(_creatureAttacked);
        clone._monsterPerformedMove = CopyEntries(_monsterPerformedMove);
        clone._damageResultStore = CopyEntries(_damageResultStore);
        clone._targetIdStore = CopyEntries(_targetIdStore);
        if (_digests != null)
        {
            clone._digests = (DigestState[])_digests.Clone();
        }
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.History);
        w.WriteInt32(_count);
        WriteDigest(w, _blockGained, BlockGainedIndex);
        WriteDigest(w, _cardAfflicted, CardAfflictedIndex);
        WriteDigest(w, _cardDiscarded, CardDiscardedIndex);
        WriteDigest(w, _cardDrawn, CardDrawnIndex);
        WriteDigest(w, _cardExhausted, CardExhaustedIndex);
        WriteDigest(w, _cardGenerated, CardGeneratedIndex);
        WriteDigest(w, _cardPlayFinished, CardPlayFinishedIndex);
        WriteDigest(w, _cardPlayStarted, CardPlayStartedIndex);
        WriteDigest(w, _creatureAttacked is null ? null : CreatureAttackedEntries, CreatureAttackedIndex);
        WriteDigest(w, _damageReceived, DamageReceivedIndex);
        WriteDigest(w, _energySpent, EnergySpentIndex);
        WriteDigest(w, _monsterPerformedMove is null ? null : MonsterPerformedMoveEntries, MonsterPerformedMoveIndex);
        WriteDigest(w, _orbChanneled, OrbChanneledIndex);
        WriteDigest(w, _potionUsed, PotionUsedIndex);
        WriteDigest(w, _powerReceived, PowerReceivedIndex);
        WriteDigest(w, _starsModified, StarsModifiedIndex);
        WriteDigest(w, _summoned, SummonedIndex);
    }

    public void WriteEntriesCanonical(CanonicalWriter w)
    {
        w.WriteTag(HistoryCanonicalTags.History);
        w.WriteInt32(_count);
        WriteEntries(w, _blockGained);
        WriteEntries(w, _cardAfflicted);
        WriteEntries(w, _cardDiscarded);
        WriteEntries(w, _cardDrawn);
        WriteEntries(w, _cardExhausted);
        WriteEntries(w, _cardGenerated);
        WriteEntries(w, _cardPlayFinished);
        WriteEntries(w, _cardPlayStarted);
        WriteEntries(w, _creatureAttacked is null ? null : CreatureAttackedEntries);
        WriteEntries(w, _damageReceived);
        WriteEntries(w, _energySpent);
        WriteEntries(w, _monsterPerformedMove is null ? null : MonsterPerformedMoveEntries);
        WriteEntries(w, _orbChanneled);
        WriteEntries(w, _potionUsed);
        WriteEntries(w, _powerReceived);
        WriteEntries(w, _starsModified);
        WriteEntries(w, _summoned);
    }

    private HistoryStamp NextStamp(CombatState cs, int actorId)
    {
        var stamp = new HistoryStamp(_count, actorId, cs.RoundNumber, cs.CurrentSide, cs.Player.TurnNumber);
        _count++;
        return stamp;
    }

    private static void Add<T>(ref List<T>? list, T entry)
    {
        (list ??= new List<T>()).Add(entry);
    }

    private static int ActorOf(CardInstance card) => card.HasOwner ? card.Owner.CombatId : NoId;

    private (int Start, int Count) StoreDamageResults(IReadOnlyList<DamageResult> damageResults)
    {
        int n = damageResults.Count;
        if (n == 0)
        {
            return (0, 0);
        }
        var store = _damageResultStore ??= new List<HistoryDamageResult>();
        int start = store.Count;
        try
        {
            for (int i = 0; i < n; i++)
            {
                store.Add(HistoryDamageResult.From(damageResults[i]));
            }
        }
        catch
        {
            store.RemoveRange(start, store.Count - start);
            throw;
        }
        return (start, n);
    }

    private (int Start, int Count) StoreTargetIds(IEnumerable<CreatureState>? targets)
    {
        if (targets == null)
        {
            return (0, MonsterPerformedMoveRecord.NoTargets);
        }
        var store = _targetIdStore ??= new List<int>();
        int start = store.Count;
        try
        {
            foreach (var target in targets)
            {
                store.Add(target.CombatId);
            }
        }
        catch
        {
            store.RemoveRange(start, store.Count - start);
            throw;
        }
        return (start, store.Count - start);
    }

    private CreatureAttackedEntry Materialize(CreatureAttackedRecord record)
        => new(record.Stamp, record.ResultCount == 0 ? default : new HistoryList<HistoryDamageResult>(_damageResultStore!, record.ResultStart, record.ResultCount));

    private MonsterPerformedMoveEntry Materialize(MonsterPerformedMoveRecord record)
    {
        HistoryList<int>? targets = record.TargetCount switch
        {
            MonsterPerformedMoveRecord.NoTargets => null,
            0 => default(HistoryList<int>),
            _ => new HistoryList<int>(_targetIdStore!, record.TargetStart, record.TargetCount)
        };
        return new MonsterPerformedMoveEntry(record.Stamp, record.MonsterId, record.MoveId, targets);
    }

    private static bool HasKeywordInCombat(CombatState cs, CardInstance card, CardKeyword keyword)
    {
        var keywords = new HashSet<CardKeyword>();
        uint mask = (uint)card.KeywordMask;
        for (int k = 0; mask != 0; k++, mask >>= 1)
        {
            if ((mask & 1u) != 0)
            {
                keywords.Add((CardKeyword)k);
            }
        }
        Hook.ModifyKeywordsInCombat(cs, card, keywords);
        return keywords.Contains(keyword);
    }

    private static List<T>? CopyEntries<T>(List<T>? source)
    {
        if (source is not { Count: > 0 })
        {
            return null;
        }
        return new List<T>(source);
    }

    private static void AddBoxed<T>(List<IHistoryEntry> buffer, IReadOnlyList<T>? list) where T : struct, IHistoryEntry
    {
        if (list == null)
        {
            return;
        }
        for (int i = 0; i < list.Count; i++)
        {
            buffer.Add(list[i]);
        }
    }

    private void WriteDigest<T>(CanonicalWriter w, IReadOnlyList<T>? list, int typeIndex) where T : struct, IHistoryEntryCanonical
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        if (n == 0)
        {
            return;
        }
        var digests = _digests ??= new DigestState[EntryTypeCount];
        var state = digests[typeIndex];
        if (state.Folded > n)
        {
            state = default;
        }
        if (state.Folded < n)
        {
            ulong digest = state.Digest;
            int start = w.Length;
            for (int i = state.Folded; i < n; i++)
            {
                list![i].WriteEntryCanonical(w);
                digest = DeterministicHash.XxHash64(w.WrittenSince(start), unchecked((long)digest));
                w.Truncate(start);
            }
            state = new DigestState(n, digest);
            digests[typeIndex] = state;
        }
        w.WriteUInt64(state.Digest);
    }

    private static void WriteEntries<T>(CanonicalWriter w, IReadOnlyList<T>? list) where T : struct, IHistoryEntryCanonical
    {
        int n = list?.Count ?? 0;
        w.WriteCount(n);
        for (int i = 0; i < n; i++)
        {
            list![i].WriteEntryCanonical(w);
        }
    }

    private readonly record struct DigestState(int Folded, ulong Digest);

    private readonly record struct CreatureAttackedRecord(HistoryStamp Stamp, int ResultStart, int ResultCount);

    private readonly record struct MonsterPerformedMoveRecord(HistoryStamp Stamp, string MonsterId, string MoveId, int TargetStart, int TargetCount)
    {
        public const int NoTargets = -1;
    }

    private sealed class CreatureAttackedView : IReadOnlyList<CreatureAttackedEntry>
    {
        private readonly CombatHistory _history;

        public CreatureAttackedView(CombatHistory history)
        {
            _history = history;
        }

        public int Count => _history._creatureAttacked?.Count ?? 0;

        public CreatureAttackedEntry this[int index]
        {
            get
            {
                var records = _history._creatureAttacked ?? throw new ArgumentOutOfRangeException(nameof(index));
                return _history.Materialize(records[index]);
            }
        }

        public IEnumerator<CreatureAttackedEntry> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class MonsterPerformedMoveView : IReadOnlyList<MonsterPerformedMoveEntry>
    {
        private readonly CombatHistory _history;

        public MonsterPerformedMoveView(CombatHistory history)
        {
            _history = history;
        }

        public int Count => _history._monsterPerformedMove?.Count ?? 0;

        public MonsterPerformedMoveEntry this[int index]
        {
            get
            {
                var records = _history._monsterPerformedMove ?? throw new ArgumentOutOfRangeException(nameof(index));
                return _history.Materialize(records[index]);
            }
        }

        public IEnumerator<MonsterPerformedMoveEntry> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
