using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sts2Sim.Core.Combat.Pipeline;

internal enum ListenerSource : byte
{
    Guarded,
    Combat,
    Run
}

internal enum ListenerKind : byte
{
    Power,
    Monster,
    Relic,
    Potion,
    Orb,
    Card,
    Affliction,
    Enchantment,
    Modifier
}

internal readonly struct ListenerEntry
{
    public ListenerEntry(AbstractModel model, ListenerKind kind, object? subject, PlayerState? player)
    {
        Model = model;
        Kind = kind;
        Subject = subject;
        Player = player;
    }

    public AbstractModel Model { get; }

    public ListenerKind Kind { get; }

    public object? Subject { get; }

    public PlayerState? Player { get; }
}

[InlineArray(ListenerSnapshot.InlineCapacity)]
internal struct ListenerEntryBuffer
{
    private ListenerEntry _element0;
}

internal ref struct ListenerSnapshot
{
    public const int InlineCapacity = 4;

    private const byte PhaseNotStarted = 0;
    private const byte PhaseRunPartThenCombat = 1;
    private const byte PhaseLastPart = 2;
    private const byte PhaseDone = 3;

    private readonly RunState _rs;
    private readonly CombatState? _cs;
    private readonly ListenerSource _source;
    private readonly HookId _hook;
    private readonly bool _masked;
    private byte _phase;
    private int _count;
    private int _index;
    private ListenerEntryBuffer _inline;
    private ListenerEntry[]? _overflow;
    private AbstractModel? _current;

    private ListenerSnapshot(RunState rs, CombatState? cs, ListenerSource source, HookId hook, bool masked)
    {
        _rs = rs;
        _cs = cs;
        _source = source;
        _hook = hook;
        _masked = masked;
        _phase = PhaseNotStarted;
        _count = 0;
        _index = 0;
        _inline = default;
        _overflow = null;
        _current = null;
    }

    public readonly AbstractModel Current => _current!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListenerSnapshot Guarded(CombatState cs, HookId hook) => new(cs.Run, cs, ListenerSource.Guarded, hook, masked: true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListenerSnapshot Combat(CombatState cs, HookId hook) => new(cs.Run, cs, ListenerSource.Combat, hook, masked: true);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListenerSnapshot Run(RunState rs, CombatState? cs, HookId hook) => new(rs, cs, ListenerSource.Run, hook, masked: true);

    public static ListenerSnapshot Of(ListenerSource source, RunState rs, CombatState? cs, HookId hook) => new(rs, cs, source, hook, masked: true);

    public static ListenerSnapshot Unmasked(ListenerSource source, RunState rs, CombatState? cs) => new(rs, cs, source, default, masked: false);

    public bool MoveNext()
    {
        while (true)
        {
            while (_index < _count)
            {
                int index = _index++;
                var entry = _overflow != null ? _overflow[index] : _inline[index];
                if (IsLive(in entry))
                {
                    _current = entry.Model;
                    return true;
                }
            }
            if (!BuildNextPart())
            {
                _current = null;
                return false;
            }
        }
    }

    public bool Contains(AbstractModel model)
    {
        while (MoveNext())
        {
            if (ReferenceEquals(_current, model))
            {
                return true;
            }
        }
        return false;
    }

    private bool BuildNextPart()
    {
        switch (_phase)
        {
            case PhaseNotStarted:
                ResetEntries();
                if (_source == ListenerSource.Run)
                {
                    AppendRunPart(includeRunOnlyListeners: _cs is null);
                    _phase = _cs is null ? PhaseLastPart : PhaseRunPartThenCombat;
                    return true;
                }
                var cs = _cs!;
                if (_source == ListenerSource.Guarded && CombatStatus.IsOverOrEnding(cs) && !cs.IsStarting)
                {
                    _phase = PhaseDone;
                    return false;
                }
                AppendCombatPart(cs);
                _phase = PhaseLastPart;
                return true;
            case PhaseRunPartThenCombat:
                ResetEntries();
                AppendCombatPart(_cs!);
                _phase = PhaseLastPart;
                return true;
            default:
                _phase = PhaseDone;
                return false;
        }
    }

    private void ResetEntries()
    {
        _count = 0;
        _index = 0;
        _overflow = null;
    }

    private void AppendRunPart(bool includeRunOnlyListeners)
    {
        var players = CollectionsMarshal.AsSpan(_rs.Players);
        for (int p = 0; p < players.Length; p++)
        {
            var player = players[p];
            if (!player.IsActiveForHooks)
            {
                continue;
            }
            var deck = CollectionsMarshal.AsSpan(player.Deck);
            for (int i = 0; i < deck.Length; i++)
            {
                var card = deck[i];
                var listener = card.Listener;
                if (listener != null)
                {
                    Append(listener, ListenerKind.Card, card, player);
                }
                var enchantment = card.Enchantment;
                if (enchantment != null)
                {
                    Append(enchantment, ListenerKind.Enchantment, card, player);
                }
            }
        }
        if (!includeRunOnlyListeners)
        {
            return;
        }
        for (int p = 0; p < players.Length; p++)
        {
            var player = players[p];
            if (!player.IsActiveForHooks)
            {
                continue;
            }
            AppendRelics(player);
            AppendPotions(player);
        }
        AppendModifiers(_rs);
    }

    private void AppendCombatPart(CombatState cs)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            AppendCreature(allies[i]);
        }
        var monsters = CollectionsMarshal.AsSpan(cs.Monsters);
        for (int i = 0; i < monsters.Length; i++)
        {
            AppendCreature(monsters[i]);
        }
        AppendModifiers(cs.Run);
    }

    private void AppendCreature(CreatureState creature)
    {
        var powers = CollectionsMarshal.AsSpan(creature.Powers);
        for (int i = 0; i < powers.Length; i++)
        {
            Append(powers[i], ListenerKind.Power, null, null);
        }
        if (creature is PlayerState player)
        {
            if (!player.IsActiveForHooks)
            {
                return;
            }
            AppendRelics(player);
            AppendPotions(player);
            var orbs = player.OrbQueue.Orbs;
            for (int i = 0; i < orbs.Count; i++)
            {
                Append(orbs[i], ListenerKind.Orb, null, player);
            }
            AppendPile(player.Hand, player);
            AppendPile(player.DrawPile, player);
            AppendPile(player.DiscardPile, player);
            AppendPile(player.ExhaustPile, player);
            AppendPile(player.PlayPile, player);
            return;
        }
        if (creature is MonsterState monster)
        {
            var listener = monster.Listener;
            if (listener != null)
            {
                Append(listener, ListenerKind.Monster, monster, null);
            }
        }
    }

    private void AppendPile(CardPile pile, PlayerState player)
    {
        var cards = CollectionsMarshal.AsSpan(pile.MutableCards);
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var listener = card.Listener;
            if (listener != null)
            {
                Append(listener, ListenerKind.Card, card, player);
            }
            var affliction = card.Affliction;
            if (affliction != null)
            {
                Append(affliction, ListenerKind.Affliction, card, player);
            }
            var enchantment = card.Enchantment;
            if (enchantment != null)
            {
                Append(enchantment, ListenerKind.Enchantment, card, player);
            }
        }
    }

    private void AppendRelics(PlayerState player)
    {
        var relics = CollectionsMarshal.AsSpan(player.Relics);
        for (int i = 0; i < relics.Length; i++)
        {
            var relic = relics[i];
            if (!relic.IsMelted)
            {
                Append(relic, ListenerKind.Relic, null, player);
            }
        }
    }

    private void AppendPotions(PlayerState player)
    {
        var slots = player.PotionSlots;
        for (int i = 0; i < slots.Length; i++)
        {
            var potion = slots[i];
            if (potion != null)
            {
                Append(potion, ListenerKind.Potion, null, player);
            }
        }
    }

    private void AppendModifiers(RunState rs)
    {
        var modifiers = CollectionsMarshal.AsSpan(rs.Modifiers);
        for (int i = 0; i < modifiers.Length; i++)
        {
            Append(modifiers[i], ListenerKind.Modifier, null, null);
        }
    }

    private void Append(AbstractModel model, ListenerKind kind, object? subject, PlayerState? player)
    {
        if (_masked && !model.OverriddenHooks.Overrides(_hook))
        {
            return;
        }
        var entry = new ListenerEntry(model, kind, subject, player);
        if (_overflow == null)
        {
            if (_count < InlineCapacity)
            {
                _inline[_count++] = entry;
                return;
            }
            var spill = new ListenerEntry[InlineCapacity * 2];
            for (int i = 0; i < InlineCapacity; i++)
            {
                spill[i] = _inline[i];
            }
            _overflow = spill;
        }
        else if (_count == _overflow.Length)
        {
            Array.Resize(ref _overflow, _overflow.Length * 2);
        }
        _overflow[_count++] = entry;
    }

    private readonly bool IsLive(in ListenerEntry entry)
    {
        switch (entry.Kind)
        {
            case ListenerKind.Power:
            {
                var power = (PowerModel)entry.Model;
                if (!power.HasOwner)
                {
                    return false;
                }
                var owner = power.Owner;
                if (!IsInCombat(_cs!, owner))
                {
                    return false;
                }
                return owner is not PlayerState ownerPlayer || ownerPlayer.IsActiveForHooks;
            }
            case ListenerKind.Monster:
                return IsInCombat(_cs!, (MonsterState)entry.Subject!);
            case ListenerKind.Relic:
            {
                var relic = (RelicModel)entry.Model;
                return !relic.HasBeenRemovedFromState && relic.Owner.IsActiveForHooks;
            }
            case ListenerKind.Potion:
            {
                var potion = (PotionModel)entry.Model;
                return !potion.HasBeenRemovedFromState && potion.Owner.IsActiveForHooks;
            }
            case ListenerKind.Orb:
            {
                var orb = (OrbModel)entry.Model;
                return !orb.HasBeenRemovedFromState && orb.Owner.IsActiveForHooks;
            }
            case ListenerKind.Card:
            {
                var card = (CardInstance)entry.Subject!;
                return !card.HasBeenRemovedFromState && OwnerOf(card, entry.Player).IsActiveForHooks;
            }
            case ListenerKind.Affliction:
            {
                var affliction = (AfflictionModel)entry.Model;
                return affliction.Card is { } card && !card.HasBeenRemovedFromState && OwnerOf(card, entry.Player).IsActiveForHooks;
            }
            case ListenerKind.Enchantment:
            {
                var enchantment = (EnchantmentModel)entry.Model;
                return enchantment.Card is { } card && !card.HasBeenRemovedFromState && OwnerOf(card, entry.Player).IsActiveForHooks;
            }
            default:
                return true;
        }
    }

    private static PlayerState OwnerOf(CardInstance card, PlayerState? holder) => card.HasOwner ? card.Owner : holder!;

    internal static bool IsInCombat(CombatState cs, CreatureState creature)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], creature))
            {
                return true;
            }
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (ReferenceEquals(monsters[i], creature))
            {
                return true;
            }
        }
        return false;
    }
}
