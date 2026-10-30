using System.Text;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class EndToEndTests
{
    private const string EncounterId = "NIBBITS_WEAK";
    private const string StrikeId = "STRIKE_IRONCLAD";
    private const string DefendId = "DEFEND_IRONCLAD";
    private const string BashId = "BASH";
    private const string AscendersBaneId = "ASCENDERS_BANE";
    private const string ButtMove = "BUTT_MOVE";
    private const string SliceMove = "SLICE_MOVE";
    private const string HissMove = "HISS_MOVE";
    private const int StepGuard = 2000;
    private const int ReferenceStartingHp = 80;
    private const int ReferenceAscendersBaneLevel = 5;
    private const int ReferenceWearyTravelerLevel = 2;

    private static readonly string[] ReferenceDeck =
    {
        StrikeId, StrikeId, StrikeId, StrikeId, StrikeId,
        DefendId, DefendId, DefendId, DefendId,
        BashId
    };

    private static string[] ReferenceDeckAt(int ascension)
        => ascension >= ReferenceAscendersBaneLevel ? ReferenceDeck.Append(AscendersBaneId).ToArray() : ReferenceDeck;

    private static int ReferenceFirstCombatHp(int ascension)
        => ascension >= ReferenceWearyTravelerLevel ? (int)(ReferenceStartingHp * 0.8m) : ReferenceStartingHp;

    public static void Test_EndToEnd_UsesTheRealStarterDeckAndEncounter()
    {
        Assert.SequenceEqual(ReferenceDeck, StartingDecks.Ironclad);
        Assert.True(StartingDecks.IsIroncladStartingDeck(ReferenceDeck.ToArray()), "the starter deck is recognised by content");
        Assert.False(StartingDecks.IsIroncladStartingDeck(ReferenceDeckAt(10)), "a deck that already carries Ascender's Bane is not the bare starter deck");
        for (int ascension = 0; ascension <= 10; ascension++)
        {
            Assert.SequenceEqual(ReferenceDeckAt(ascension), StartingDecks.IroncladAtRunStart(ascension), $"run-start deck at A{ascension}");
            Assert.SequenceEqual(ReferenceDeckAt(ascension), CombatLoop.DeckForCombat(ReferenceDeck.ToArray(), ascension), $"combat deck from the starter deck at A{ascension}");
            Assert.SequenceEqual(ReferenceDeckAt(ascension), CombatLoop.DeckForCombat(StartingDecks.IroncladAtRunStart(ascension), ascension), $"a run-start deck is used as given at A{ascension}");
            Assert.Equal(ReferenceFirstCombatHp(ascension), StartingDecks.IroncladHpAfterNeowArrival(ascension), $"HP after Neow's arrival heal at A{ascension}");
        }
        Assert.Equal(EncounterId, EncounterRegistry.Get(EncounterId).Id);
        var sim = CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId);
        Assert.Equal(1, sim.State.Monsters.Count);
        Assert.Equal("NIBBIT", sim.State.Monsters[0].MonsterId);
        Assert.Equal(10, CombatDeckSize(sim));
        Assert.Equal(80, sim.State.Player.CurrentHp);
        Assert.Equal(80, sim.State.Player.MaxHp);

        var a4 = CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 4);
        Assert.Equal(10, CombatDeckSize(a4), "A4 adds no curse");
        var a5 = CombatSim.Create(1UL, ReferenceDeck.ToArray(), EncounterId, 5);
        Assert.Equal(11, CombatDeckSize(a5), "A5 appends Ascender's Bane to the starter deck");
        Assert.Equal(1, CountInCombat(a5, AscendersBaneId));

        var a10 = CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 10);
        Assert.Equal(11, CombatDeckSize(a10), "A10 deck carries Ascender's Bane");
        Assert.Equal(1, CountInCombat(a10, AscendersBaneId));
        Assert.Equal(80, a10.State.Player.CurrentHp, "without an override the combat starts at the character's max HP");
        Assert.Equal(80, a10.State.Player.MaxHp);

        var a10RunStart = CombatSim.Create(1UL, StartingDecks.IroncladAtRunStart(10), EncounterId, 10, currentHp: StartingDecks.IroncladHpAfterNeowArrival(10));
        Assert.Equal(11, CombatDeckSize(a10RunStart), "a deck that already has the curse is not given a second one");
        Assert.Equal(64, a10RunStart.State.Player.CurrentHp, "HP override: Neow's 0.8 arrival heal at A2+");
        Assert.Equal(80, a10RunStart.State.Player.MaxHp);
        Assert.Equal(a10RunStart.StateHash(), CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 10, currentHp: 64).StateHash(), "starter deck + rule = run-start deck");

        var custom = CombatSim.Create(1UL, Enumerable.Repeat(StrikeId, 10).ToArray(), EncounterId, 10, currentHp: 30, maxHp: 90);
        Assert.Equal(10, CombatDeckSize(custom), "any other deck is used exactly as given");
        Assert.Equal(30, custom.State.Player.CurrentHp);
        Assert.Equal(90, custom.State.Player.MaxHp);
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 0, currentHp: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 0, currentHp: 81));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatSim.Create(1UL, StartingDecks.Ironclad, EncounterId, 0, maxHp: 0));
    }

    private static int CombatDeckSize(CombatSim sim) => sim.State.Player.Hand.Count + sim.State.Player.DrawPile.Count;

    private static int CountInCombat(CombatSim sim, string id)
        => sim.State.Player.Hand.Cards.Count(c => c.Id == id) + sim.State.Player.DrawPile.Cards.Count(c => c.Id == id);

    public static void Test_EndToEnd_GreedyPlayMatchesIndependentReferenceUntilVictory()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var end = RunLockstep(seed, 0, Greedy);
            Assert.Equal(CombatOutcome.Victory, end.Outcome, $"seed {seed}");
        }
        for (ulong seed = 101; seed <= 110; seed++)
        {
            var end = RunLockstep(seed, 10, Greedy);
            Assert.Equal(CombatOutcome.Victory, end.Outcome, $"seed {seed} A10");
        }
    }

    public static void Test_EndToEnd_GreedySeedOneEndsInAPinnedVictory()
    {
        var actions = new List<GameAction>();
        var end = RunLockstep(1UL, 0, Greedy, actions);
        Assert.Equal(PinnedSeedOneVictory, end);
        Assert.Equal(PinnedSeedOneActionCount, actions.Count);
    }

    public static void Test_EndToEnd_RandomPlayMatchesIndependentReference()
    {
        int victories = 0;
        int defeats = 0;
        for (ulong seed = 1; seed <= 150; seed++)
        {
            var end = RunLockstep(seed, 0, RandomPolicy(seed));
            if (end.Outcome == CombatOutcome.Victory)
            {
                victories++;
            }
            else
            {
                defeats++;
            }
        }
        for (ulong seed = 1001; seed <= 1050; seed++)
        {
            var end = RunLockstep(seed, 10, RandomPolicy(seed));
            if (end.Outcome == CombatOutcome.Victory)
            {
                victories++;
            }
            else
            {
                defeats++;
            }
        }
        Assert.True(victories > 0, "random play never won");
        Assert.True(defeats > 0, "random play never lost");
    }

    public static void Test_EndToEnd_PassivePlayerLosesOnTheHandComputedRound()
    {
        AssertPassiveDefeat(5UL, 0, new[] { 68, 62, 62, 48, 40, 40, 24, 14, 14 }, sliceBlock: 5, finalStrength: 6, exhausted: Array.Empty<string>());
        AssertPassiveDefeat(6UL, 10, new[] { 51, 44, 44, 28, 18, 18 }, sliceBlock: 6, finalStrength: 6, exhausted: new[] { AscendersBaneId });
    }

    public static void Test_EndToEnd_IdenticalRunsProduceIdenticalHashSequences()
    {
        var openings = new HashSet<ulong>();
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var actionsA = new List<GameAction>();
            var hashesA = new List<ulong>();
            var endA = RunLockstep(seed, 0, RandomPolicy(seed), actionsA, hashesA);
            var actionsB = new List<GameAction>();
            var hashesB = new List<ulong>();
            var endB = RunLockstep(seed, 0, RandomPolicy(seed), actionsB, hashesB);
            Assert.SequenceEqual(actionsA, actionsB, $"seed {seed} actions");
            Assert.SequenceEqual(hashesA, hashesB, $"seed {seed} hashes");
            Assert.Equal(endA, endB, $"seed {seed} end state");
            Assert.True(hashesA.Count == actionsA.Count + 1, $"seed {seed} one hash per decision");
            openings.Add(hashesA[0]);

            var replay = CombatSim.Create(seed, StartingDecks.IroncladAtRunStart(0), EncounterId);
            var scratch = new CanonicalWriter();
            for (int i = 0; i < actionsA.Count; i++)
            {
                Assert.Equal(hashesA[i], replay.StateHash(), $"seed {seed} replay hash {i}");
                Assert.Equal(hashesA[i], replay.StateHash(scratch), $"seed {seed} replay scratch hash {i}");
                replay.Step(actionsA[i]);
            }
            Assert.Equal(hashesA[^1], replay.StateHash(), $"seed {seed} replay final hash");
        }
        Assert.Equal(3, openings.Count, "different seeds give different opening hashes");
    }

    public static void Test_EndToEnd_CloneAtEveryDecisionReplaysTheSameHashes()
    {
        var actions = new List<GameAction>();
        var hashes = new List<ulong>();
        RunLockstep(7UL, 0, RandomPolicy(7UL), actions, hashes);
        var sim = CombatSim.Create(7UL, StartingDecks.IroncladAtRunStart(0), EncounterId);
        for (int i = 0; i < actions.Count; i++)
        {
            var clone = sim.Clone();
            Assert.Equal(hashes[i], clone.StateHash(), $"clone hash at decision {i}");
            for (int j = i; j < actions.Count; j++)
            {
                clone.Step(actions[j]);
                Assert.Equal(hashes[j + 1], clone.StateHash(), $"clone from decision {i}, after action {j}");
            }
            Assert.True(clone.IsTerminal, $"clone from decision {i} reaches the end");
            Assert.Equal(hashes[i], sim.StateHash(), $"source untouched by its clone at decision {i}");
            sim.Step(actions[i]);
        }
        Assert.Equal(hashes[^1], sim.StateHash());
    }

    public static void Test_EndToEnd_CanonicalHashesArePinnedAcrossProcesses()
    {
        var hashes = new List<ulong>();
        RunLockstep(1UL, 0, Greedy, null, hashes);
        Assert.Equal(PinnedSeedOneOpeningHash, hashes[0], "opening state hash of seed 1 (refresh only after an intended canonical-format change)");
        Assert.Equal(PinnedSeedOneFinalHash, hashes[^1], "final state hash of seed 1 greedy (refresh only after an intended canonical-format change)");
    }

    private static readonly Snapshot PinnedSeedOneVictory = new(
        Round: 3, Turn: 3, Side: CombatSide.Player, Outcome: CombatOutcome.Victory,
        PlayerHp: 67, PlayerMaxHp: 80, PlayerBlock: 0, Energy: 0,
        Hand: "DS", Draw: "BSDDD", Discard: "SS", Play: "S", Exhaust: "",
        Monsters: 0, MonsterHp: 0, MonsterMaxHp: 0, MonsterBlock: 0, MonsterStrength: 0, MonsterVulnerable: 0,
        Intent: null);

    private const int PinnedSeedOneActionCount = 10;

    private const ulong PinnedSeedOneOpeningHash = 0xA22C1E9190E84745UL;

    private const ulong PinnedSeedOneFinalHash = 0xF16FB5A5465E6826UL;

    private static void AssertPassiveDefeat(ulong seed, int ascension, int[] hpAfterEnemyTurn, int sliceBlock, int finalStrength, string[] exhausted)
    {
        var trace = new List<Snapshot>();
        CombatSim? final = null;
        var end = RunLockstep(seed, ascension, static (_, _) => GameAction.EndTurn, trace: trace, atEnd: s => final = s);
        string context = $"A{ascension} seed {seed}";
        int survived = hpAfterEnemyTurn.Length;
        Assert.Equal(survived + 2, trace.Count, $"{context}: opening, {survived} survived rounds, then the fatal enemy turn");
        Assert.Equal(ReferenceFirstCombatHp(ascension), trace[0].PlayerHp, $"{context} HP entering the combat");
        Assert.Equal(ReferenceStartingHp, trace[0].PlayerMaxHp, $"{context} max HP");
        string[] cycle = { ButtMove, SliceMove, HissMove };
        for (int round = 1; round <= survived; round++)
        {
            var before = trace[round - 1];
            var after = trace[round];
            Assert.Equal(round, before.Round, $"{context} round");
            Assert.Equal(cycle[(round - 1) % 3], before.Intent, $"{context} round {round} intent");
            Assert.Equal(CombatOutcome.InProgress, after.Outcome, $"{context} round {round}");
            Assert.Equal(hpAfterEnemyTurn[round - 1], after.PlayerHp, $"{context} HP after the round {round} enemy turn");
            Assert.Equal(round % 3 == 2 ? sliceBlock : 0, after.MonsterBlock, $"{context} Nibbit block during player turn {round + 1}");
            Assert.Equal(trace[0].MonsterHp, after.MonsterHp, $"{context} Nibbit HP is untouched");
        }
        Assert.Equal(cycle[survived % 3], trace[survived].Intent, $"{context} fatal intent");
        Assert.Equal(CombatOutcome.Defeat, end.Outcome, context);
        Assert.Equal(survived + 1, end.Round, $"{context} defeat round");
        Assert.Equal(CombatSide.Enemy, end.Side, $"{context} the side stays Enemy");
        Assert.Equal(0, end.PlayerHp);
        Assert.Equal(finalStrength, end.MonsterStrength, $"{context} Nibbit Strength");
        Assert.Equal(Codes(exhausted.ToList()), end.Exhaust, $"{context} exhaust pile");
        var sim = Assert.NotNull(final);
        Assert.SequenceEqual(exhausted, sim.State.Player.ExhaustPile.Cards.Select(c => c.Id), $"{context} sim exhaust pile");
        Assert.Throws<InvalidOperationException>(() => sim.Step(GameAction.EndTurn));
    }

    private static Snapshot RunLockstep(ulong seed, int ascension, Func<IReadOnlyList<GameAction>, CombatState, GameAction> policy, List<GameAction>? actions = null, List<ulong>? hashes = null, List<Snapshot>? trace = null, Action<CombatSim>? atEnd = null)
    {
        var sim = CombatSim.Create(seed, StartingDecks.Ironclad, EncounterId, ascension, currentHp: StartingDecks.IroncladHpAfterNeowArrival(ascension));
        var reference = new NibbitsWeakReference(seed, ascension);
        var legal = new List<GameAction>(16);
        var scratch = new CanonicalWriter();
        string context = $"seed {seed} A{ascension}";
        var observed = reference.Observe();
        Assert.Equal(observed, Observe(sim.State), context + " opening");
        trace?.Add(observed);
        hashes?.Add(sim.StateHash(scratch));
        int step = 0;
        while (!sim.IsTerminal)
        {
            if (step >= StepGuard)
            {
                Assert.Fail($"{context}: no terminal state after {StepGuard} decisions");
            }
            sim.LegalActions(legal);
            Assert.SequenceEqual(reference.LegalActions(), legal, $"{context} step {step} legal actions");
            var action = policy(legal, sim.State);
            sim.Step(action);
            reference.Apply(action);
            actions?.Add(action);
            hashes?.Add(sim.StateHash(scratch));
            observed = reference.Observe();
            Assert.Equal(observed, Observe(sim.State), $"{context} step {step} after {action}");
            trace?.Add(observed);
            step++;
        }
        Assert.Equal(0, sim.LegalActions().Count, context + " terminal legal actions");
        atEnd?.Invoke(sim);
        return Observe(sim.State);
    }

    private static GameAction Greedy(IReadOnlyList<GameAction> legal, CombatState state)
    {
        var hand = state.Player.Hand;
        bool vulnerable = state.Monsters.Count > 0 && state.Monsters[0].HasPower<VulnerablePower>();
        GameAction? First(string id)
        {
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Kind == ActionKind.PlayCard && hand[legal[i].CardIndex].Id == id)
                {
                    return legal[i];
                }
            }
            return null;
        }
        if (!vulnerable && First(BashId) is { } bash)
        {
            return bash;
        }
        return First(StrikeId) ?? First(BashId) ?? First(DefendId) ?? GameAction.EndTurn;
    }

    private static Func<IReadOnlyList<GameAction>, CombatState, GameAction> RandomPolicy(ulong seed)
    {
        var rng = new Rng(unchecked(seed * 7919UL + 13UL));
        return (legal, _) => legal[rng.NextInt(legal.Count)];
    }

    private static Snapshot Observe(CombatState s)
    {
        var p = s.Player;
        var m = s.Monsters.Count > 0 ? s.Monsters[0] : null;
        return new Snapshot(
            s.RoundNumber, p.TurnNumber, s.CurrentSide, s.Outcome,
            p.CurrentHp, p.MaxHp, p.Block, p.Energy,
            Codes(p.Hand.Cards), Codes(p.DrawPile.Cards), Codes(p.DiscardPile.Cards), Codes(p.PlayPile.Cards), Codes(p.ExhaustPile.Cards),
            s.Monsters.Count,
            m?.CurrentHp ?? 0, m?.MaxHp ?? 0, m?.Block ?? 0,
            m?.GetPowerAmount<StrengthPower>() ?? 0, m?.GetPowerAmount<VulnerablePower>() ?? 0,
            m != null && m.HasAi ? m.Ai.NextMove?.Id : null);
    }

    private static string Codes(IReadOnlyList<CardInstance> cards)
    {
        var sb = new StringBuilder(cards.Count);
        for (int i = 0; i < cards.Count; i++)
        {
            sb.Append(Code(cards[i].Id));
        }
        return sb.ToString();
    }

    private static string Codes(List<string> ids)
    {
        var sb = new StringBuilder(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            sb.Append(Code(ids[i]));
        }
        return sb.ToString();
    }

    private static char Code(string id) => id switch
    {
        StrikeId => 'S',
        DefendId => 'D',
        BashId => 'B',
        AscendersBaneId => 'A',
        _ => '?'
    };

    private readonly record struct Snapshot(
        int Round,
        int Turn,
        CombatSide Side,
        CombatOutcome Outcome,
        int PlayerHp,
        int PlayerMaxHp,
        int PlayerBlock,
        int Energy,
        string Hand,
        string Draw,
        string Discard,
        string Play,
        string Exhaust,
        int Monsters,
        int MonsterHp,
        int MonsterMaxHp,
        int MonsterBlock,
        int MonsterStrength,
        int MonsterVulnerable,
        string? Intent);

    private sealed class NibbitsWeakReference
    {
        private const int PlayerMaxEnergy = 3;
        private const int HandDraw = 5;
        private const int MaxHandSize = 10;

        private readonly Rng _shuffle;
        private readonly int _buttDamage;
        private readonly int _sliceDamage;
        private readonly int _sliceBlock;
        private readonly int _hissStrength;
        private readonly List<string> _draw = new();
        private readonly List<string> _hand = new();
        private readonly List<string> _discard = new();
        private readonly List<string> _play = new();
        private readonly List<string> _exhaust = new();
        private bool _performedFirstMove;
        private int _round = 1;
        private int _turn = 1;
        private CombatSide _side = CombatSide.Player;
        private CombatOutcome _outcome = CombatOutcome.InProgress;
        private readonly int _playerMaxHp = ReferenceStartingHp;
        private int _playerHp;
        private int _playerBlock;
        private int _energy;
        private readonly int _monsterMaxHp;
        private int _monsterHp;
        private int _monsterBlock;
        private int _monsterStrength;
        private int _monsterVulnerable;
        private string _nextMove = ButtMove;

        public NibbitsWeakReference(ulong seed, int ascension)
        {
            var rng = new RunRngSet(seed);
            _shuffle = rng.Shuffle;
            bool tough = ascension >= 8;
            bool deadly = ascension >= 9;
            int minHp = tough ? 44 : 42;
            int maxHp = tough ? 48 : 46;
            _monsterMaxHp = minHp + rng.Niche.NextInt(0, maxHp - minHp + 1);
            _monsterHp = _monsterMaxHp;
            _buttDamage = deadly ? 13 : 12;
            _sliceDamage = deadly ? 7 : 6;
            _sliceBlock = tough ? 6 : 5;
            _hissStrength = deadly ? 3 : 2;
            _playerHp = ReferenceFirstCombatHp(ascension);
            _draw.AddRange(ReferenceDeckAt(ascension));
            UnstableShuffle(_draw);
            StartPlayerTurn();
        }

        public List<GameAction> LegalActions()
        {
            var legal = new List<GameAction>();
            if (_outcome != CombatOutcome.InProgress || _side != CombatSide.Player)
            {
                return legal;
            }
            for (int i = 0; i < _hand.Count; i++)
            {
                if (_hand[i] == AscendersBaneId || Cost(_hand[i]) > _energy)
                {
                    continue;
                }
                legal.Add(_hand[i] == DefendId ? GameAction.Play(i) : GameAction.Play(i, 0));
            }
            legal.Add(GameAction.EndTurn);
            return legal;
        }

        public void Apply(GameAction action)
        {
            if (action.Kind == ActionKind.EndTurn)
            {
                EndTurn();
            }
            else
            {
                PlayCard(action.CardIndex);
            }
        }

        public Snapshot Observe()
        {
            bool present = _outcome != CombatOutcome.Victory;
            return new Snapshot(
                _round, _turn, _side, _outcome,
                _playerHp, _playerMaxHp, _playerBlock, _energy,
                Codes(_hand), Codes(_draw), Codes(_discard), Codes(_play), Codes(_exhaust),
                present ? 1 : 0,
                present ? _monsterHp : 0, present ? _monsterMaxHp : 0, present ? _monsterBlock : 0,
                present ? _monsterStrength : 0, present ? _monsterVulnerable : 0,
                present ? _nextMove : null);
        }

        private static int Cost(string id) => id == BashId ? 2 : 1;

        private void StartPlayerTurn()
        {
            if (_performedFirstMove)
            {
                _nextMove = _nextMove switch
                {
                    ButtMove => SliceMove,
                    SliceMove => HissMove,
                    _ => ButtMove
                };
            }
            if (_turn != 1)
            {
                _playerBlock = 0;
            }
            _energy = PlayerMaxEnergy;
            for (int i = 0; i < HandDraw; i++)
            {
                if (_hand.Count >= MaxHandSize || _draw.Count + _discard.Count == 0)
                {
                    break;
                }
                if (_draw.Count == 0)
                {
                    Reshuffle();
                }
                _hand.Add(_draw[0]);
                _draw.RemoveAt(0);
            }
        }

        private void PlayCard(int handIndex)
        {
            string id = _hand[handIndex];
            _hand.RemoveAt(handIndex);
            _energy -= Cost(id);
            _play.Add(id);
            switch (id)
            {
                case StrikeId:
                    HitMonster(6m);
                    break;
                case DefendId:
                    _playerBlock += 5;
                    break;
                case BashId:
                    HitMonster(8m);
                    if (_monsterHp > 0)
                    {
                        _monsterVulnerable += 2;
                    }
                    break;
            }
            if (_monsterHp == 0)
            {
                _outcome = CombatOutcome.Victory;
                return;
            }
            _play.RemoveAt(_play.Count - 1);
            _discard.Add(id);
        }

        private void HitMonster(decimal damage)
        {
            if (_monsterVulnerable > 0)
            {
                damage *= 1.5m;
            }
            decimal blocked = Math.Min(_monsterBlock, damage);
            _monsterBlock -= (int)blocked;
            _monsterHp = Math.Max(0, _monsterHp - (int)(damage - blocked));
        }

        private void HitPlayer(int damage)
        {
            decimal amount = Math.Max(0, damage);
            decimal blocked = Math.Min(_playerBlock, amount);
            _playerBlock -= (int)blocked;
            _playerHp = Math.Max(0, _playerHp - (int)(amount - blocked));
            if (_playerHp == 0)
            {
                _outcome = CombatOutcome.Defeat;
            }
        }

        private void EndTurn()
        {
            _exhaust.AddRange(_hand.Where(static id => id == AscendersBaneId));
            _hand.RemoveAll(static id => id == AscendersBaneId);
            _discard.AddRange(_hand);
            _hand.Clear();
            _side = CombatSide.Enemy;
            _monsterBlock = 0;
            _performedFirstMove = true;
            switch (_nextMove)
            {
                case ButtMove:
                    HitPlayer(_buttDamage + _monsterStrength);
                    break;
                case SliceMove:
                    HitPlayer(_sliceDamage + _monsterStrength);
                    if (_outcome == CombatOutcome.InProgress)
                    {
                        _monsterBlock += _sliceBlock;
                    }
                    break;
                default:
                    _monsterStrength += _hissStrength;
                    break;
            }
            if (_outcome != CombatOutcome.InProgress)
            {
                return;
            }
            if (_monsterVulnerable > 0)
            {
                _monsterVulnerable--;
            }
            _side = CombatSide.Player;
            _round++;
            _turn++;
            StartPlayerTurn();
        }

        private void Reshuffle()
        {
            var cards = new List<string>(_discard.Count + _draw.Count);
            cards.AddRange(_discard);
            cards.AddRange(_draw);
            _discard.Clear();
            _draw.Clear();
            cards.Sort(string.CompareOrdinal);
            UnstableShuffle(cards);
            _draw.AddRange(cards);
        }

        private void UnstableShuffle(List<string> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _shuffle.NextInt(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }
    }
}
