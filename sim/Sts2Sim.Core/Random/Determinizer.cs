namespace Sts2Sim.Core.Random;

/// <summary>Knobs of <see cref="Determinizer"/>. The default resamples everything the player cannot see.</summary>
public sealed record DeterminizeOptions
{
    public static DeterminizeOptions Default { get; } = new();

    /// <summary>
    /// Number of cards at the top of each draw pile (index 0 = the next draw) whose order the caller knows (for example a
    /// card it put on top with Headbutt) and that therefore keep their positions. The kernel and the game do not track
    /// which placements the player remembers (sim/VEC.md "Determinized clones"), so the default is 0.
    /// </summary>
    public int KeepDrawTop { get; init; }

    /// <summary>Same as <see cref="KeepDrawTop"/> for the bottom of each draw pile.</summary>
    public int KeepDrawBottom { get; init; }

    /// <summary>Also give the pre-rolled run queues a fresh order: relic grab bags and the unvisited events of the current and later acts.</summary>
    public bool ResampleRunQueues { get; init; } = true;

    internal void WriteCanonical(CanonicalWriter w)
    {
        w.WriteInt32(KeepDrawTop);
        w.WriteInt32(KeepDrawBottom);
        w.WriteBool(ResampleRunQueues);
    }
}

/// <summary>A determinization applied by a pending prompt's re-run when the tape hands out answer <see cref="AnswerIndex"/>.</summary>
internal readonly record struct DeterminizeHook(int AnswerIndex, ulong Seed, DeterminizeOptions Options)
{
    public static void Fire(IReadOnlyList<DeterminizeHook> hooks, int answerIndex, RunState rs, CombatState? cs)
    {
        for (int i = 0; i < hooks.Count; i++)
        {
            var hook = hooks[i];
            if (hook.AnswerIndex == answerIndex)
            {
                Determinizer.Apply(rs, cs, hook.Seed, hook.Options);
            }
        }
    }

    public static void Write(CanonicalWriter w, ushort tag, IReadOnlyList<DeterminizeHook>? hooks)
    {
        if (hooks == null || hooks.Count == 0)
        {
            return;
        }
        w.WriteTag(tag);
        w.WriteCount(hooks.Count);
        for (int i = 0; i < hooks.Count; i++)
        {
            w.WriteInt32(hooks[i].AnswerIndex);
            w.WriteUInt64(hooks[i].Seed);
            hooks[i].Options.WriteCanonical(w);
        }
    }

    public static List<DeterminizeHook> Append(IReadOnlyList<DeterminizeHook>? hooks, DeterminizeHook hook)
    {
        var list = new List<DeterminizeHook>((hooks?.Count ?? 0) + 1);
        if (hooks != null)
        {
            list.AddRange(hooks);
        }
        list.Add(hook);
        return list;
    }
}

/// <summary>
/// Resamples the hidden part of a state in place, keeping everything the player can see: the information-set
/// determinization behind <c>RunSim.CloneDeterminized</c> / <c>CombatSim.CloneDeterminized</c>.
/// <list type="bullet">
/// <item>Draw-pile order: a uniform permutation of each draw pile (the multiset stays), except
/// <see cref="DeterminizeOptions.KeepDrawTop"/>/<see cref="DeterminizeOptions.KeepDrawBottom"/> cards at the ends.</item>
/// <item>Every live RNG object restarted from a seed-derived value, in place (aliases stay aliases, call counters stay):
/// the 12 <see cref="RunRngSet"/> streams and its hashed run seed (root of the lazily derived act-map, event, monster and
/// encounter RNGs), each player's 3 <see cref="PlayerRngSet"/> streams and set seed, the current event's
/// <c>EventModel.Rng</c>, reward RNG overrides (<c>Reward.RngOverride</c>, <c>CardCreationOptions.RngOverride</c>) and
/// monsters' private RNGs (<see cref="PrivateRngExtraState"/>).</item>
/// <item>Optionally (<see cref="DeterminizeOptions.ResampleRunQueues"/>): relic grab-bag deque orders and the unvisited
/// tail of the event queue of the current and later acts.</item>
/// </list>
/// Public state is untouched: the current monster intents (<c>NextMove</c>), hand, discard and exhaust order, prompt
/// options, generated shop / reward contents and the map. The same (state, seed, options) always gives the same result.
/// </summary>
public static class Determinizer
{
    private const ulong SeedSalt = 0xD7E7_3A1C_5B0F_9E21UL;

    public static void Apply(RunState rs, ulong seed, DeterminizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rs);
        Apply(rs, rs.Combat, seed, options ?? DeterminizeOptions.Default);
    }

    public static void Apply(CombatState cs, ulong seed, DeterminizeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cs);
        Apply(cs.Run, cs, seed, options ?? DeterminizeOptions.Default);
    }

    internal static void Apply(RunState rs, CombatState? cs, ulong seed, DeterminizeOptions options)
    {
        ulong x = seed ^ SeedSalt;
        var d = new Rng(MegaRandom.Splitmix64(ref x));
        Func<ulong> next = d.NextUnsignedLong;
        var seen = new HashSet<Rng>(ReferenceEqualityComparer.Instance);

        rs.Rng?.Determinize(next, seen);
        var players = rs.Players;
        for (int i = 0; i < players.Count; i++)
        {
            players[i].PlayerRng?.Determinize(next, seen);
        }
        if (cs != null && !players.Contains(cs.Player))
        {
            cs.Player.PlayerRng?.Determinize(next, seen);
        }

        var rooms = rs.CurrentRooms;
        for (int i = 0; i < rooms.Count; i++)
        {
            switch (rooms[i])
            {
                case EventRoom er:
                    Reseed(er.CanonicalEvent.Rng, d, seen);
                    Reseed(er.LocalMutableEvent?.Rng, d, seen);
                    break;
                case CombatRoom cr:
                    ReseedRewards(cr.ExtraRewards, d, seen);
                    break;
            }
        }
        var stack = rs.RewardsStack;
        for (int i = 0; i < stack.Count; i++)
        {
            ReseedRewards(stack[i].Rewards, d, seen);
        }

        if (cs != null)
        {
            ReseedMonsters(cs.Monsters, d, seen);
            ReseedMonsters(cs.Allies, d, seen);
            ReseedMonsters(cs.EscapedCreatures, d, seen);
            ShuffleDrawPile(cs.Player, d, options);
            for (int i = 0; i < players.Count; i++)
            {
                if (!ReferenceEquals(players[i], cs.Player))
                {
                    ShuffleDrawPile(players[i], d, options);
                }
            }
        }

        if (options.ResampleRunQueues)
        {
            rs.SharedRelicGrabBag?.ReshuffleDeques(d);
            for (int i = 0; i < players.Count; i++)
            {
                players[i].RelicGrabBag?.ReshuffleDeques(d);
            }
            var acts = rs.Acts;
            for (int a = Math.Max(0, rs.CurrentActIndex); a < acts.Count; a++)
            {
                acts[a].Rooms.ReshuffleUnvisitedEvents(d, rs.VisitedEventIds);
            }
        }
    }

    private static void Reseed(Rng? rng, Rng d, HashSet<Rng> seen)
    {
        ulong value = d.NextUnsignedLong();
        if (rng != null && seen.Add(rng))
        {
            rng.Reseed(value);
        }
    }

    private static void ReseedRewards(IReadOnlyList<Reward> rewards, Rng d, HashSet<Rng> seen)
    {
        for (int i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            Reseed(reward.OverrideRng, d, seen);
            if (reward is CardReward card)
            {
                Reseed(card.Options.RngOverride, d, seen);
                Reseed(card.RerollOptions.RngOverride, d, seen);
            }
        }
    }

    private static void ReseedMonsters<T>(IReadOnlyList<T> creatures, Rng d, HashSet<Rng> seen) where T : CreatureState
    {
        for (int i = 0; i < creatures.Count; i++)
        {
            if (creatures[i] is MonsterState { Extra: PrivateRngExtraState holder })
            {
                Reseed(holder.PrivateRng, d, seen);
            }
        }
    }

    private static void ShuffleDrawPile(PlayerState player, Rng d, DeterminizeOptions options)
    {
        var cards = player.DrawPile.MutableCards;
        int n = cards.Count;
        int lo = Math.Clamp(options.KeepDrawTop, 0, n);
        int hi = n - Math.Clamp(options.KeepDrawBottom, 0, n - lo);
        for (int i = hi - 1; i > lo; i--)
        {
            int j = lo + d.NextInt(i - lo + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }
}
