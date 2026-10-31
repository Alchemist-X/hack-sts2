using System.Buffers;

namespace Sts2Sim.Play;

public readonly record struct PlayStats(
    string Outcome,
    int HpStart,
    int HpEnd,
    int Turns,
    int Decisions,
    int InvalidAttempts,
    ulong Seed,
    string EncounterId,
    int Ascension)
{
    public string ToJsonLine()
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, Encoder = PlaySession.JsonEncoder }))
        {
            w.WriteStartObject();
            w.WriteString("outcome", Outcome);
            w.WriteNumber("hp_start", HpStart);
            w.WriteNumber("hp_end", HpEnd);
            w.WriteNumber("turns", Turns);
            w.WriteNumber("decisions", Decisions);
            w.WriteNumber("invalid_attempts", InvalidAttempts);
            w.WriteNumber("seed", Seed);
            w.WriteString("encounter_id", EncounterId);
            w.WriteNumber("ascension", Ascension);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}

public sealed class PlayGame
{
    public const string OutcomeVictory = "victory";

    public const string OutcomeDefeat = "defeat";

    public const string OutcomeCap = "cap";

    public const string OutcomeInProgress = "in_progress";

    private PlayGame(PlaySession session, CombatSim sim)
    {
        Session = session;
        Sim = sim;
        HpStart = sim.State.Player.CurrentHp;
    }

    public PlaySession Session { get; }

    public CombatSim Sim { get; }

    public int HpStart { get; }

    public bool IsCapped => !Sim.IsTerminal && Session.Actions.Count >= Session.ActionCap;

    public bool IsOver => Sim.IsTerminal || IsCapped;

    public string Outcome => Sim.Outcome switch
    {
        CombatOutcome.Victory => OutcomeVictory,
        CombatOutcome.Defeat => OutcomeDefeat,
        _ => IsCapped ? OutcomeCap : OutcomeInProgress
    };

    public string StateHashHex => Sim.StateHash().ToString("X16", CultureInfo.InvariantCulture);

    public static CombatSim CreateSim(PlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!EncounterRegistry.TryGet(session.EncounterId, out _))
        {
            throw new SessionFormatException($"unknown encounter '{session.EncounterId}'; registered: {string.Join(", ", EncounterRegistry.AllIds)}");
        }
        try
        {
            return CombatSim.Create(session.Seed, StartingDecks.Ironclad, session.EncounterId, session.Ascension, session.CurrentHp, session.MaxHp);
        }
        catch (ArgumentException ex)
        {
            throw new SessionFormatException("cannot create the combat: " + ex.Message);
        }
    }

    public static PlayGame Replay(PlaySession session)
    {
        var game = new PlayGame(session, CreateSim(session));
        var sim = game.Sim;
        var actions = session.Actions;
        if (actions.Count > session.ActionCap)
        {
            throw new SessionReplayException($"the session holds {actions.Count} actions, more than its cap of {session.ActionCap}");
        }
        for (int i = 0; i < actions.Count; i++)
        {
            var stored = actions[i];
            if (sim.IsTerminal)
            {
                throw new SessionReplayException($"action {i + 1} of {actions.Count} comes after the combat ended ({sim.Outcome})");
            }
            var legal = sim.LegalActions();
            if (stored.K < 1 || stored.K > legal.Count || legal[stored.K - 1] != stored.Action)
            {
                throw new SessionReplayException($"action {i + 1} (k={stored.K}, {stored.Action}) is not legal action {stored.K} on replay ({legal.Count} legal actions)");
            }
            sim.Step(stored.Action);
        }
        if (session.StateHash != null && !string.Equals(session.StateHash, game.StateHashHex, StringComparison.Ordinal))
        {
            throw new SessionReplayException($"the replayed state hash {game.StateHashHex} differs from the stored {session.StateHash}");
        }
        return game;
    }

    public int LegalActionCount => IsOver ? 0 : Sim.LegalActions().Count;

    public SessionAction Apply(int k)
    {
        if (IsOver)
        {
            throw new InvalidOperationException($"The combat is over ({Outcome}).");
        }
        var legal = Sim.LegalActions();
        if (k < 1 || k > legal.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, $"Legal action numbers are 1..{legal.Count}.");
        }
        var action = legal[k - 1];
        string label = StateRenderer.ActionLabel(Sim.State, action, nameTarget: true);
        Sim.Step(action);
        var entry = new SessionAction(k, action, label);
        Session.Actions.Add(entry);
        Session.StateHash = StateHashHex;
        return entry;
    }

    public PlayStats Stats()
    {
        var player = Sim.State.Player;
        return new PlayStats(Outcome, HpStart, player.CurrentHp, player.TurnNumber, Session.Actions.Count, Session.InvalidAttempts,
            Session.Seed, Session.EncounterId, Session.Ascension);
    }
}
