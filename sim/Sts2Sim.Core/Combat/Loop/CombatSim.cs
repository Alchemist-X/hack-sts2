namespace Sts2Sim.Core;

public sealed class CombatSim
{
    private readonly CombatState _state;
    private readonly List<GameAction> _legalBuffer = new(16);
    private CanonicalWriter? _hashScratch;

    private CombatSim(CombatState state)
    {
        _state = state;
    }

    public static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0, int? currentHp = null, int? maxHp = null)
        => new(CombatLoop.CreateCombat(new RunRngSet(seed), deckIds, encounterId, ascension, currentHp, maxHp));

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0, int? currentHp = null, int? maxHp = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new(CombatLoop.CreateCombat(rng.DeepClone(), deckIds, encounterId, ascension, currentHp, maxHp));
    }

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new(CombatLoop.CreateCombat(rng.DeepClone(), deck, encounterId, ascension, currentHp, maxHp));
    }

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, CombatRunContext context)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return new(CombatLoop.CreateCombat(rng.DeepClone(), deck, encounterId, ascension, currentHp, maxHp, context));
    }

    public static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension, Action<CombatState> populateMonsters)
    {
        ArgumentNullException.ThrowIfNull(populateMonsters);
        return new(CombatLoop.CreateCombat(seed, deckIds, encounterId, ascension, populateMonsters));
    }

    public static CombatSim FromState(CombatState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state);
    }

    public CombatState State => _state;

    public CombatOutcome Outcome => _state.Outcome;

    public bool IsTerminal => _state.Outcome != CombatOutcome.InProgress;

    public IReadOnlyList<GameAction> LegalActions()
    {
        _legalBuffer.Clear();
        LegalActionGenerator.Fill(_state, _legalBuffer);
        return _legalBuffer;
    }

    public void LegalActions(List<GameAction> buffer)
    {
        buffer.Clear();
        LegalActionGenerator.Fill(_state, buffer);
    }

    public bool IsLegal(GameAction action) => LegalActionGenerator.IsLegal(_state, action);

    public void Step(GameAction action) => CombatLoop.Step(_state, action);

    public CombatSim Clone() => new(_state.DeepClone());

    public ulong StateHash() => StateHasher.Hash(_state, _hashScratch ??= new CanonicalWriter());

    public ulong StateHash(CanonicalWriter scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        return StateHasher.Hash(_state, scratch);
    }

    public void WriteCanonical(CanonicalWriter w) => _state.WriteCanonical(w);
}
