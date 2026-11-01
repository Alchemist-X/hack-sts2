using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class W1GateTests
{
    private const int StepCap = 1000;
    private const int CloneCheckOneIn = 3;
    private const int MaxCloneSteps = 4;
    private const int LockstepLength = 3;
    private const int IllegalSamplesPerState = 4;
    private const int MaxPickCandidate = 12;
    private const int MaxPilotRelicsPerPlayer = 12;
    private const int HighAscension = 10;

    private const int PilotRelicMinimum = 6;
    private const int PilotPotionMinimum = 4;
    private const int PilotOrbMinimum = 5;
    private const string OstyMonsterId = "OSTY";
    private const string DarkEmbracePowerId = "DARK_EMBRACE_POWER";
    private const string GremlinHornRelicId = "GREMLIN_HORN";
    private const string HellraiserPowerId = "HELLRAISER_POWER";
    private const string SeekerStrikeCardId = "SEEKER_STRIKE";

    private static readonly string[] PilotRelicIds = { "GAMBLING_CHIP", "PAPER_PHROG", "PAPER_KRANE" };
    private static readonly string[] PilotPotionIds = { "FAIRY_IN_A_BOTTLE", "ATTACK_POTION", "ASHWATER" };
    private static readonly string[] PilotPromptCardIds = { "ARMAMENTS", "ACROBATICS", "HEADBUTT", "SEANCE", "DISCOVERY", SeekerStrikeCardId };
    private static readonly string[] PilotPowerIds = { HellraiserPowerId, "TOOLS_OF_THE_TRADE_POWER", "ENTROPY_POWER", DarkEmbracePowerId, "FOCUS_POWER" };

    private static readonly ulong[] F1Seeds = { 1UL, 2UL, 3UL, 4UL };
    private static readonly ulong[] F4Seeds = { 11UL, 12UL, 13UL };
    private static readonly ulong[] F5Seeds = { 21UL, 22UL, 23UL, 24UL };
    private static readonly ulong[] B2Seeds = { 31UL, 32UL, 33UL, 34UL, 35UL, 36UL, 37UL, 38UL };

    public static void Test_W1Gate_F1_CloneHashEqualityOnRandomPlayouts()
    {
        var encounters = RequireEncounters();
        var counts = new GateCounts();
        var legal = new List<GameAction>(32);
        var twinLegal = new List<GameAction>(32);
        foreach (var encounterId in encounters)
        {
            foreach (var seed in F1Seeds)
            {
                int ascension = AscensionFor(seed);
                var policy = new GatePolicy(PolicySeed(encounterId, seed, 0xF1UL));
                var sim = CreateGateSim(encounterId, seed, ascension, counts);
                string run = $"{encounterId} seed {seed} A{ascension}";
                counts.Playouts++;
                ProbeHashCoverage(sim, run, counts);
                CombatSim? twin = null;
                int twinSteps = 0;
                int step = 0;
                for (; step < StepCap && !sim.IsTerminal; step++)
                {
                    string label = $"{run} step {step}";
                    if (twin == null && policy.OneIn(CloneCheckOneIn))
                    {
                        twin = CheckCloneIndependence(sim, policy, label, counts);
                        twinSteps = 0;
                    }
                    sim.LegalActions(legal);
                    Assert.True(legal.Count > 0, $"{label}: a combat that is not over must offer a legal action");
                    var action = legal[policy.Next(legal.Count)];
                    if (twin != null)
                    {
                        twin.LegalActions(twinLegal);
                        Assert.SequenceEqual(legal, twinLegal, $"{label}: lockstep twin legal actions");
                    }
                    CountAction(action, counts);
                    sim.Step(action);
                    counts.Steps++;
                    if (twin != null)
                    {
                        twin.Step(action);
                        counts.LockstepSteps++;
                        Assert.Equal(sim.StateHash(), twin.StateHash(), $"{label}: lockstep twin hash after {action}");
                        if (++twinSteps >= LockstepLength)
                        {
                            twin = null;
                        }
                    }
                }
                if (!sim.IsTerminal)
                {
                    counts.Capped++;
                }
                CheckCloneIndependence(sim, policy, $"{run} final (step {step})", counts);
            }
        }
        Console.WriteLine($"     W1Gate F1: {encounters.Count} encounters x {F1Seeds.Length} seeds = {counts.Playouts} playouts ({counts.Capped} capped), {counts.Steps} steps, {counts.CloneChecks} clone checks, {counts.CloneSteps} clone steps, {counts.LockstepSteps} lockstep steps, {counts.HashProbes} hash-coverage probes, {counts.PromptActions} prompt answers, {counts.PotionActions} potion actions; {counts.Describe()}");
    }

    public static void Test_W1Gate_F4_LegalActionClosure()
    {
        var encounters = RequireEncounters();
        var counts = new GateCounts();
        var legal = new List<GameAction>(32);
        var candidates = new List<GameAction>(256);
        var illegal = new List<GameAction>(256);
        long listedStepped = 0;
        long candidatesChecked = 0;
        long illegalStepped = 0;
        long decisionStates = 0;
        long promptStates = 0;
        foreach (var encounterId in encounters)
        {
            foreach (var seed in F4Seeds)
            {
                int ascension = AscensionFor(seed);
                var policy = new GatePolicy(PolicySeed(encounterId, seed, 0xF4UL));
                var sim = CreateGateSim(encounterId, seed, ascension, counts);
                string run = $"{encounterId} seed {seed} A{ascension}";
                counts.Playouts++;
                for (int step = 0; step <= StepCap; step++)
                {
                    string label = $"{run} step {step}";
                    sim.LegalActions(legal);
                    var listed = new List<GameAction>(legal);
                    if (sim.IsTerminal)
                    {
                        Assert.Equal(0, listed.Count, $"{label}: a terminal state lists no action");
                        break;
                    }
                    if (step == StepCap)
                    {
                        counts.Capped++;
                        break;
                    }
                    decisionStates++;
                    Assert.True(listed.Count > 0, $"{label}: a non-terminal state must have a legal action");
                    var listedSet = new HashSet<GameAction>(listed);
                    Assert.Equal(listed.Count, listedSet.Count, $"{label}: LegalActions lists an action twice");
                    if (listed.Exists(IsPromptAction))
                    {
                        promptStates++;
                    }
                    ulong hash = sim.StateHash();
                    var bytes = SimBytes(sim);
                    foreach (var action in listed)
                    {
                        Assert.True(sim.IsLegal(action), $"{label}: listed action {action} is not IsLegal");
                        var branch = sim.Clone();
                        try
                        {
                            branch.Step(action);
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail($"{label}: listed action {action} threw {ex.GetType().Name}: {ex.Message}");
                        }
                        listedStepped++;
                    }
                    Assert.Equal(hash, sim.StateHash(), $"{label}: stepping branches changed the source");
                    BuildCandidates(sim.State, candidates);
                    illegal.Clear();
                    foreach (var candidate in candidates)
                    {
                        candidatesChecked++;
                        if (listedSet.Contains(candidate))
                        {
                            continue;
                        }
                        if (sim.IsLegal(candidate))
                        {
                            Assert.True(candidate.Kind == ActionKind.ChoosePick && candidate.TargetIndex == GameAction.NoIndex, $"{label}: {candidate} is legal but LegalActions does not list it");
                            continue;
                        }
                        illegal.Add(candidate);
                    }
                    Assert.Equal(hash, sim.StateHash(), $"{label}: IsLegal changed the state");
                    int samples = Math.Min(IllegalSamplesPerState, illegal.Count);
                    for (int i = 0; i < samples; i++)
                    {
                        var candidate = illegal[policy.Next(illegal.Count)];
                        Assert.Throws<InvalidOperationException>(() => sim.Step(candidate), $"{label}: illegal action {candidate}");
                        Assert.Equal(hash, sim.StateHash(), $"{label}: illegal action {candidate} changed the hash");
                        Assert.True(bytes.AsSpan().SequenceEqual(SimBytes(sim)), $"{label}: illegal action {candidate} changed the canonical bytes");
                        illegalStepped++;
                    }
                    var chosen = listed[policy.Next(listed.Count)];
                    CountAction(chosen, counts);
                    sim.Step(chosen);
                    counts.Steps++;
                }
            }
        }
        Assert.True(illegalStepped > 0, "the closure check must step at least one illegal action");
        Console.WriteLine($"     W1Gate F4: {counts.Playouts} playouts ({counts.Capped} capped), {decisionStates} decision states, {promptStates} prompt states, {listedStepped} listed actions stepped on clones, {candidatesChecked} candidates checked with IsLegal, {illegalStepped} illegal actions stepped (all threw, hash unchanged), {counts.PromptActions} prompt answers, {counts.PotionActions} potion actions; {counts.Describe()}");
    }

    public static void Test_W1Gate_F5_InvariantsHoldAfterEveryStep()
    {
        var encounters = RequireEncounters();
        var counts = new GateCounts();
        var legal = new List<GameAction>(32);
        long checks = 0;
        foreach (var encounterId in encounters)
        {
            foreach (var seed in F5Seeds)
            {
                int ascension = AscensionFor(seed);
                var policy = new GatePolicy(PolicySeed(encounterId, seed, 0xF5UL));
                var sim = CreateGateSim(encounterId, seed, ascension, counts);
                string run = $"{encounterId} seed {seed} A{ascension}";
                counts.Playouts++;
                var ledger = new InvariantLedger();
                ledger.Check(sim.State, $"{run} after Create");
                int step = 0;
                for (; step < StepCap && !sim.IsTerminal; step++)
                {
                    sim.LegalActions(legal);
                    Assert.True(legal.Count > 0, $"{run} step {step}: a combat that is not over must offer a legal action");
                    var action = legal[policy.Next(legal.Count)];
                    CountAction(action, counts);
                    sim.Step(action);
                    counts.Steps++;
                    ledger.Check(sim.State, $"{run} after step {step} ({action})");
                }
                if (!sim.IsTerminal)
                {
                    counts.Capped++;
                }
                checks += ledger.Checks;
            }
        }
        Console.WriteLine($"     W1Gate F5: {counts.Playouts} playouts ({counts.Capped} capped), {counts.Steps} steps, {checks} invariant checks (HP, block, energy, stars, gold, potion slots, relic/potion/card owners, card conservation across piles and deck, creature ids, power owners, RNG counters), {counts.PromptActions} prompt answers, {counts.PotionActions} potion actions; {counts.Describe()}");
    }

    public static void Test_W1Gate_F5_LedgerRejectsBrokenStates()
    {
        var counts = new GateCounts();
        var sim = CreateGateSim(RequireEncounters()[0], 5UL, 0, counts);
        Assert.True(sim.State.Player.Hand.Count > 0 && sim.State.Monsters.Count > 0, "the ledger probe needs a hand card and a monster");
        var violations = new (string Name, string Fragment, Action<CombatState, InvariantLedger> Break)[]
        {
            ("card in two piles", "more than one pile", (s, l) => s.Player.DiscardPile.AddInternal(s.Player.Hand[0])),
            ("card resurrected", "came back", (s, l) =>
            {
                var card = s.Player.Hand.RemoveAtInternal(0);
                l.Check(s, "card removed");
                s.Player.DiscardPile.AddInternal(card);
            }),
            ("deck card in a pile", "also in a combat pile", (s, l) => s.Player.Deck.Add(s.Player.Hand[0])),
            ("hp above max", "HP", (s, l) => s.Player.CurrentHp = s.Player.MaxHp + 1),
            ("negative energy", "energy", (s, l) => s.Player.Energy = -1),
            ("negative gold", "gold", (s, l) => s.Player.Gold = -1),
            ("duplicate creature id", "used by another creature", (s, l) => s.AddMonster(new MonsterState(s.Player.CombatId, s.Monsters[0].Def, 5, 5, null, null))),
            ("power held twice", "owner is another creature object", (s, l) => s.Monsters[0].Powers.Add(Assert.NotNull(PowerCmd.Apply<StrengthPower>(s, s.Player, 2m, s.Player, null)))),
            ("rng counter rewound", "counter went down", (s, l) =>
            {
                var snapshot = s.Rng.Shuffle.ToSnapshot();
                s.Rng.Shuffle.NextInt(10);
                l.Check(s, "rng advanced");
                s.Rng.Shuffle.LoadFromSnapshot(snapshot);
            }),
            ("relic of another player", "not owned by the player", (s, l) =>
            {
                var stranger = new GateCounterRelic();
                stranger.AttachTo(new PlayerState(99, StartingDecks.IroncladCharacterId, 10, 10, 3));
                s.Player.Relics.Add(stranger);
            }),
        };
        foreach (var (name, fragment, breakState) in violations)
        {
            var clone = sim.Clone();
            var ledger = new InvariantLedger();
            ledger.Check(clone.State, $"before '{name}'");
            breakState(clone.State, ledger);
            var ex = Assert.Throws<AssertionException>(() => ledger.Check(clone.State, name), $"the F5 ledger accepted a state with '{name}'");
            Assert.True(ex.Message.Contains(fragment, StringComparison.Ordinal), $"'{name}' was rejected for another reason: {ex.Message}");
        }
    }

    public static void Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline()
    {
        bool hasDarkEmbrace = PowerRegistry.AllIds.Contains(DarkEmbracePowerId);
        bool hasGremlinHorn = RelicRegistry.Models.Contains(GremlinHornRelicId);
        var missing = new List<string>();
        if (!hasDarkEmbrace && !hasGremlinHorn)
        {
            missing.Add($"{DarkEmbracePowerId} (power) or {GremlinHornRelicId} (relic)");
        }
        if (!PowerRegistry.AllIds.Contains(HellraiserPowerId))
        {
            missing.Add($"{HellraiserPowerId} (power)");
        }
        if (!CardRegistry.Defs.Contains(SeekerStrikeCardId))
        {
            missing.Add($"{SeekerStrikeCardId} (card)");
        }
        if (missing.Count > 0)
        {
            Assert.Fail($"B2 chain content is not registered: {string.Join(", ", missing)}");
        }
        var deck = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            deck.Add(SeekerStrikeCardId);
        }
        for (int i = 0; i < 4; i++)
        {
            deck.Add(AscendersBane.CardId);
        }
        string encounterId = EncounterRegistry.Defs.Contains(NibbitsWeak.EncounterIdEntry) ? NibbitsWeak.EncounterIdEntry : RequireEncounters()[0];
        var legal = new List<GameAction>(32);
        int playouts = 0;
        int flagged = 0;
        int steps = 0;
        int promptAnswers = 0;
        foreach (var seed in B2Seeds)
        {
            var sim = CombatSim.Create(seed, deck, encounterId);
            var cs = sim.State;
            var player = cs.Player;
            if (hasDarkEmbrace)
            {
                PowerCmd.Apply(cs, PowerRegistry.Create(DarkEmbracePowerId), player, 1m, player, null);
            }
            if (hasGremlinHorn)
            {
                RelicCmd.Obtain(cs.Run, RelicRegistry.Create(GremlinHornRelicId), player);
            }
            PowerCmd.Apply(cs, PowerRegistry.Create(HellraiserPowerId), player, 1m, player, null);
            playouts++;
            var policy = new GatePolicy(PolicySeed(encounterId, seed, 0xB2UL));
            for (int step = 0; step < StepCap && !sim.IsTerminal; step++)
            {
                sim.LegalActions(legal);
                Assert.True(legal.Count > 0, $"B2 chain seed {seed} step {step}: a combat that is not over must offer a legal action");
                var action = legal[policy.Next(legal.Count)];
                if (IsPromptAction(action))
                {
                    promptAnswers++;
                }
                try
                {
                    sim.Step(action);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"B2 chain seed {seed} step {step}: {action} threw {ex.GetType().Name}: {ex.Message}");
                }
                steps++;
                if (sim.State.Run.Fidelity.Contains(FidelityFlag.DeferredListenerPromptInline))
                {
                    flagged++;
                    break;
                }
            }
        }
        Console.WriteLine($"     W1Gate B2: {playouts} playouts on {encounterId}, {steps} steps, {promptAnswers} prompt answers, {flagged} recorded {nameof(FidelityFlag.DeferredListenerPromptInline)}");
        Assert.True(flagged > 0, $"no B2 chain playout recorded {nameof(FidelityFlag.DeferredListenerPromptInline)} ({playouts} playouts, {steps} steps, {promptAnswers} prompt answers)");
    }

    public static void Test_W1Gate_PilotContentIsRegistered()
    {
        var missing = new List<string>();
        if (RelicRegistry.Count < PilotRelicMinimum)
        {
            missing.Add($"relics: {RelicRegistry.Count} registered, B5 needs at least {PilotRelicMinimum}");
        }
        missing.AddRange(PilotRelicIds.Where(id => !RelicRegistry.Models.Contains(id)).Select(id => "relic " + id));
        if (PotionRegistry.Count < PilotPotionMinimum)
        {
            missing.Add($"potions: {PotionRegistry.Count} registered, B5 needs at least {PilotPotionMinimum}");
        }
        missing.AddRange(PilotPotionIds.Where(id => !PotionRegistry.Models.Contains(id)).Select(id => "potion " + id));
        missing.AddRange(PilotPromptCardIds.Where(id => !CardRegistry.Defs.Contains(id)).Select(id => "card " + id));
        missing.AddRange(PilotPowerIds.Where(id => !PowerRegistry.AllIds.Contains(id)).Select(id => "power " + id));
        if (EnchantmentRegistry.Count < 1)
        {
            missing.Add("enchantments: none registered, B5 needs one");
        }
        if (AfflictionRegistry.Count < 1)
        {
            missing.Add("afflictions: none registered, B5 needs one");
        }
        if (OrbRegistry.Count < PilotOrbMinimum)
        {
            missing.Add($"orbs: {OrbRegistry.Count} registered, B5 needs {PilotOrbMinimum}");
        }
        if (!MonsterRegistry.Defs.Contains(OstyMonsterId))
        {
            missing.Add("monster " + OstyMonsterId);
        }
        Assert.True(missing.Count == 0, $"W1 pilot content (CONTRACTS Part B §B5) is not registered ({missing.Count}): {string.Join("; ", missing)}");
    }

    private static IReadOnlyList<string> RequireEncounters()
    {
        var ids = EncounterRegistry.AllIds;
        Assert.True(ids.Count > 0, "the W1 gates need at least one registered encounter");
        return ids;
    }

    private static int AscensionFor(ulong seed) => seed % 2UL == 0UL ? HighAscension : 0;

    private static ulong PolicySeed(string encounterId, ulong seed, ulong salt)
    {
        unchecked
        {
            ulong h = 0xCBF29CE484222325UL ^ salt;
            foreach (char ch in encounterId)
            {
                h ^= ch;
                h *= 0x100000001B3UL;
            }
            return h ^ (seed * 0x9E3779B97F4A7C15UL);
        }
    }

    private static CombatSim CreateGateSim(string encounterId, ulong seed, int ascension, GateCounts counts)
    {
        var promptCards = PilotPromptCardIds.Where(id => CardRegistry.Defs.Contains(id)).ToList();
        IReadOnlyList<string> deck = StartingDecks.Ironclad;
        if (promptCards.Count > 0)
        {
            var withPilots = new List<string>(StartingDecks.Ironclad);
            withPilots.AddRange(promptCards);
            deck = withPilots;
        }
        var sim = CombatSim.Create(seed, deck, encounterId, ascension);
        EquipPlayer(sim.State, seed, counts);
        counts.PilotPromptCards = promptCards.Count;
        return sim;
    }

    private static void EquipPlayer(CombatState cs, ulong seed, GateCounts counts)
    {
        var player = cs.Player;
        player.Gold = 50 + (int)(seed % 200UL);
        var relicIds = RelicRegistry.AllIds.ToList();
        if (relicIds.Count > MaxPilotRelicsPerPlayer)
        {
            var shuffle = new GatePolicy(seed ^ 0x5E1EC7UL);
            for (int i = relicIds.Count - 1; i > 0; i--)
            {
                int j = shuffle.Next(i + 1);
                (relicIds[i], relicIds[j]) = (relicIds[j], relicIds[i]);
            }
            relicIds.RemoveRange(MaxPilotRelicsPerPlayer, relicIds.Count - MaxPilotRelicsPerPlayer);
        }
        foreach (var id in relicIds)
        {
            RelicCmd.Obtain(cs.Run, RelicRegistry.Create(id), player);
        }
        RelicCmd.Obtain(cs.Run, new GateCounterRelic(), player);
        var potionIds = PotionRegistry.AllIds;
        var slots = player.PotionSlots;
        int filled = seed % 2UL == 0UL ? slots.Length : Math.Max(0, slots.Length - 1);
        int pilotPotions = 0;
        for (int i = 0; i < filled; i++)
        {
            PotionModel potion;
            if (potionIds.Count > 0)
            {
                potion = PotionRegistry.Create(potionIds[(int)((seed + (ulong)i) % (ulong)potionIds.Count)]);
                pilotPotions++;
            }
            else
            {
                potion = new GateChargePotion();
            }
            var procured = PotionCmd.TryToProcure(cs.Run, potion, player, i);
            Assert.True(procured.Success, $"gate potion {potion.Id} fills slot {i}");
        }
        counts.PilotRelics = relicIds.Count;
        counts.PilotPotions += pilotPotions;
        counts.DoublePotions += filled - pilotPotions;
    }

    private static void ProbeHashCoverage(CombatSim sim, string run, GateCounts counts)
    {
        var legal = new List<GameAction>(32);
        sim.LegalActions(legal);
        if (sim.IsTerminal || legal.Exists(IsPromptAction))
        {
            return;
        }
        ulong hash = sim.StateHash();
        var probes = new (string Name, Func<CombatState, bool> Mutate)[]
        {
            ("gold", s => { s.Player.Gold += 1; return true; }),
            ("relic counter", s => Mutate<GateCounterRelic>(s.Player.Relics, r => r.Counter++)),
            ("relic log", s => Mutate<GateCounterRelic>(s.Player.Relics, r => r.Log.Add(7))),
            ("relic list", s => { s.Player.Relics.RemoveAt(s.Player.Relics.Count - 1); return true; }),
            ("potion charges", s => Mutate<GateChargePotion>(s.Player.PotionSlots, p => p.Charges++)),
            ("potion slot", s => ClearFirstPotionSlot(s.Player)),
        };
        foreach (var (name, mutate) in probes)
        {
            var probe = sim.Clone();
            Assert.Equal(hash, probe.StateHash(), $"{run}: clone before the '{name}' probe");
            if (!mutate(probe.State))
            {
                continue;
            }
            Assert.NotEqual(hash, probe.StateHash(), $"{run}: mutating the {name} of the player did not change the hash");
            Assert.Equal(hash, sim.StateHash(), $"{run}: mutating the {name} of a clone changed the source");
            counts.HashProbes++;
        }
    }

    private static bool ClearFirstPotionSlot(PlayerState player)
    {
        var slots = player.PotionSlots;
        if (slots.Length == 0 || slots[0] is null)
        {
            return false;
        }
        slots[0] = null;
        return true;
    }

    private static bool Mutate<T>(IEnumerable<object?> items, Action<T> mutate) where T : class
    {
        foreach (var item in items)
        {
            if (item is T t)
            {
                mutate(t);
                return true;
            }
        }
        return false;
    }

    private static CombatSim CheckCloneIndependence(CombatSim sim, GatePolicy policy, string label, GateCounts counts)
    {
        ulong hash = sim.StateHash();
        var bytes = SimBytes(sim);
        var clone = sim.Clone();
        counts.CloneChecks++;
        Assert.Equal(hash, clone.StateHash(), $"{label}: Hash(Clone(x)) differs from Hash(x)");
        Assert.True(bytes.AsSpan().SequenceEqual(SimBytes(clone)), $"{label}: the canonical bytes of the clone differ from the source");
        Assert.Equal(sim.IsTerminal, clone.IsTerminal, $"{label}: clone terminal flag");
        Assert.Equal(sim.Outcome, clone.Outcome, $"{label}: clone outcome");
        Assert.SequenceEqual(sim.LegalActions().ToList(), clone.LegalActions().ToList(), $"{label}: clone legal actions");
        var cloneOfClone = clone.Clone();
        Assert.Equal(hash, cloneOfClone.StateHash(), $"{label}: Hash(Clone(Clone(x))) differs from Hash(x)");
        var twin = sim.Clone();
        var buffer = new List<GameAction>(32);
        int k = 1 + policy.Next(MaxCloneSteps);
        for (int i = 0; i < k && !clone.IsTerminal; i++)
        {
            clone.LegalActions(buffer);
            Assert.True(buffer.Count > 0, $"{label}: the clone is not over but offers no legal action");
            clone.Step(buffer[policy.Next(buffer.Count)]);
            counts.CloneSteps++;
        }
        var state = clone.State;
        state.Player.Gold += 3;
        Mutate<GateCounterRelic>(state.Player.Relics, r =>
        {
            r.Counter += 5;
            r.Log.Add(r.Counter);
        });
        Mutate<GateChargePotion>(state.Player.PotionSlots, p => p.Charges += 2);
        Assert.Equal(hash, sim.StateHash(), $"{label}: stepping or mutating the clone changed the source hash");
        Assert.True(bytes.AsSpan().SequenceEqual(SimBytes(sim)), $"{label}: stepping or mutating the clone changed the source canonical bytes");
        Assert.Equal(hash, cloneOfClone.StateHash(), $"{label}: stepping the clone changed the clone of the clone");
        Assert.Equal(hash, twin.StateHash(), $"{label}: stepping the clone changed a sibling clone");
        return twin;
    }

    private static byte[] SimBytes(CombatSim sim)
    {
        var w = new CanonicalWriter();
        sim.WriteCanonical(w);
        return w.ToArray();
    }

    private static bool IsPromptAction(GameAction action)
        => action.Kind is ActionKind.ChoosePick or ActionKind.ChooseConfirm or ActionKind.ChooseCancel;

    private static void CountAction(GameAction action, GateCounts counts)
    {
        if (IsPromptAction(action))
        {
            counts.PromptActions++;
        }
        else if (action.Kind is ActionKind.UsePotion or ActionKind.DiscardPotion)
        {
            counts.PotionActions++;
        }
    }

    private static void BuildCandidates(CombatState cs, List<GameAction> candidates)
    {
        candidates.Clear();
        int hand = cs.Player.Hand.Count;
        int monsters = cs.Monsters.Count;
        int slots = cs.Player.PotionSlots.Length;
        int allies = cs.Allies.Count;
        for (int card = -1; card <= hand; card++)
        {
            for (int target = -2; target <= monsters; target++)
            {
                candidates.Add(GameAction.Play(card, target));
            }
        }
        candidates.Add(GameAction.EndTurn);
        candidates.Add(new GameAction(ActionKind.EndTurn, 0, GameAction.NoIndex));
        candidates.Add(new GameAction(ActionKind.EndTurn, GameAction.NoIndex, 0));
        for (int slot = -1; slot <= slots; slot++)
        {
            for (int target = -3 - allies; target <= monsters; target++)
            {
                candidates.Add(GameAction.UsePotion(slot, target));
            }
            candidates.Add(GameAction.DiscardPotion(slot));
            candidates.Add(new GameAction(ActionKind.DiscardPotion, slot, 0));
        }
        for (int option = -1; option <= MaxPickCandidate; option++)
        {
            candidates.Add(GameAction.Pick(option));
        }
        candidates.Add(new GameAction(ActionKind.ChoosePick, 0, 0));
        candidates.Add(GameAction.Confirm);
        candidates.Add(new GameAction(ActionKind.ChooseConfirm, 0, GameAction.NoIndex));
        candidates.Add(new GameAction(ActionKind.ChooseConfirm, GameAction.NoIndex, 0));
        candidates.Add(GameAction.Cancel);
        candidates.Add(new GameAction(ActionKind.ChooseCancel, 0, GameAction.NoIndex));
        candidates.Add(new GameAction(ActionKind.ChooseCancel, GameAction.NoIndex, 0));
        candidates.Add(new GameAction((ActionKind)250, 0, 0));
    }

    private sealed class GateCounts
    {
        public int Playouts;
        public int Capped;
        public long Steps;
        public long CloneChecks;
        public long CloneSteps;
        public long LockstepSteps;
        public long PromptActions;
        public long PotionActions;
        public long HashProbes;
        public int PilotRelics;
        public int PilotPromptCards;
        public long PilotPotions;
        public long DoublePotions;

        public string Describe()
            => $"player slots per playout: {PilotRelics} pilot relics + 1 test-double relic, potions filled {PilotPotions} pilot / {DoublePotions} test-double in total; deck: starter + {PilotPromptCards} pilot prompt cards; registered relics {RelicRegistry.Count}, potions {PotionRegistry.Count}, orbs {OrbRegistry.Count}, enchantments {EnchantmentRegistry.Count}, afflictions {AfflictionRegistry.Count}";
    }

    private sealed class GatePolicy
    {
        private ulong _state;

        public GatePolicy(ulong seed)
        {
            _state = seed;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        public bool OneIn(int n) => Next(n) == 0;

        private ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    private sealed class InvariantLedger
    {
        private readonly int[] _rngCounters = new int[RunRngSet.StreamCount];
        private readonly HashSet<int> _goneCardIds = new();
        private bool _hasRng;
        private HashSet<int>? _cardIds;
        private int _nextCardInstanceId = int.MinValue;
        private int _nextCreatureId = int.MinValue;

        public int Checks { get; private set; }

        public void Check(CombatState cs, string label)
        {
            Checks++;
            var player = cs.Player;
            Assert.True(cs.Allies.Count > 0 && ReferenceEquals(cs.Allies[0], player), $"{label}: Allies[0] must be the player");
            var creatures = new List<CreatureState> { player };
            foreach (var monster in cs.Monsters)
            {
                AddDistinct(creatures, monster);
            }
            foreach (var ally in cs.Allies)
            {
                AddDistinct(creatures, ally);
            }
            Assert.True(cs.NextCreatureId >= _nextCreatureId, $"{label}: NextCreatureId went down from {_nextCreatureId} to {cs.NextCreatureId}");
            _nextCreatureId = cs.NextCreatureId;
            var creatureIds = new HashSet<int>();
            var powers = new HashSet<PowerModel>(ReferenceEqualityComparer.Instance);
            foreach (var creature in creatures)
            {
                string who = $"{label}: creature {creature.CombatId} ({(creature is MonsterState m ? m.MonsterId : "player")})";
                Assert.True(creature.CurrentHp >= 0 && creature.CurrentHp <= creature.MaxHp, $"{who} HP {creature.CurrentHp} outside [0, {creature.MaxHp}]");
                Assert.True(creature.Block >= 0, $"{who} block {creature.Block} is negative");
                Assert.True(creatureIds.Add(creature.CombatId), $"{who} id is used by another creature");
                Assert.True(creature.CombatId < cs.NextCreatureId, $"{who} id is not below NextCreatureId {cs.NextCreatureId}");
                foreach (var power in creature.Powers)
                {
                    Assert.True(power.HasOwner && ReferenceEquals(power.Owner, creature), $"{who} holds {power.Id}, whose owner is another creature object");
                    Assert.True(powers.Add(power), $"{who} holds {power.Id}, which another creature also holds");
                }
            }
            Assert.True(player.Energy >= 0, $"{label}: energy {player.Energy} is negative");
            Assert.True(player.Stars >= 0, $"{label}: stars {player.Stars} is negative");
            Assert.True(player.Gold >= 0, $"{label}: gold {player.Gold} is negative");
            int potions = 0;
            foreach (var potion in player.PotionSlots)
            {
                if (potion is null)
                {
                    continue;
                }
                potions++;
                Assert.True(potion.HasOwner && ReferenceEquals(potion.Owner, player), $"{label}: potion {potion.Id} is not owned by the player");
            }
            Assert.True(potions <= player.MaxPotionCount, $"{label}: {potions} potions in {player.MaxPotionCount} slots");
            foreach (var relic in player.Relics)
            {
                Assert.True(relic.HasOwner && ReferenceEquals(relic.Owner, player), $"{label}: relic {relic.Id} is not owned by the player");
            }
            var objects = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
            var cardIds = new HashSet<int>();
            foreach (var pile in player.AllPiles)
            {
                foreach (var card in pile.Cards)
                {
                    string which = $"{label}: {pile.Type} card {card.Id}#{card.InstanceId}";
                    Assert.True(objects.Add(card), $"{which} is in more than one pile");
                    Assert.True(cardIds.Add(card.InstanceId), $"{which} shares its instance id with another combat card");
                    Assert.True(card.InstanceId < cs.NextCardInstanceId, $"{which} was not allocated (NextCardInstanceId {cs.NextCardInstanceId})");
                    Assert.False(_goneCardIds.Contains(card.InstanceId), $"{which} left every pile earlier and came back");
                    Assert.True(!card.HasOwner || ReferenceEquals(card.Owner, player), $"{which} is owned by another player object");
                }
            }
            var deckIds = new HashSet<int>();
            foreach (var card in player.Deck)
            {
                Assert.True(objects.Add(card), $"{label}: deck card {card.Id}#{card.InstanceId} is also in a combat pile");
                Assert.True(deckIds.Add(card.InstanceId), $"{label}: deck card {card.Id}#{card.InstanceId} shares its instance id with another deck card");
            }
            Assert.True(cs.NextCardInstanceId >= _nextCardInstanceId, $"{label}: NextCardInstanceId went down from {_nextCardInstanceId} to {cs.NextCardInstanceId}");
            if (_cardIds != null)
            {
                foreach (var id in _cardIds)
                {
                    if (!cardIds.Contains(id))
                    {
                        _goneCardIds.Add(id);
                    }
                }
            }
            _cardIds = cardIds;
            _nextCardInstanceId = cs.NextCardInstanceId;
            var rng = cs.Rng;
            for (int i = 0; i < _rngCounters.Length; i++)
            {
                int counter = rng.Get((RunRngType)i).Counter;
                if (_hasRng)
                {
                    Assert.True(counter >= _rngCounters[i], $"{label}: RNG stream {RunRngSet.StreamName((RunRngType)i)} counter went down from {_rngCounters[i]} to {counter}");
                }
                _rngCounters[i] = counter;
            }
            _hasRng = true;
        }

        private static void AddDistinct(List<CreatureState> creatures, CreatureState creature)
        {
            foreach (var existing in creatures)
            {
                if (ReferenceEquals(existing, creature))
                {
                    return;
                }
            }
            creatures.Add(creature);
        }
    }

    private sealed class GateCounterRelic : RelicModel
    {
        public override string Id => "W1GATE_TEST_COUNTER_RELIC";

        public override RelicRarity Rarity => RelicRarity.Common;

        public int Counter { get; set; }

        public List<int> Log { get; private set; } = new();

        protected override void CopyMutableReferenceState(RelicModel source)
        {
            Log = new List<int>(((GateCounterRelic)source).Log);
        }

        protected override void WriteExtraCanonical(CanonicalWriter w)
        {
            w.WriteInt32(Counter);
            w.WriteCount(Log.Count);
            for (int i = 0; i < Log.Count; i++)
            {
                w.WriteInt32(Log[i]);
            }
        }
    }

    private sealed class GateChargePotion : PotionModel
    {
        public override string Id => "W1GATE_TEST_CHARGE_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.Self;

        public int Charges { get; set; }

        protected override void WriteExtraCanonical(CanonicalWriter w) => w.WriteInt32(Charges);
    }
}
