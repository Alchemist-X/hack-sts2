// W2 fills
namespace Sts2Sim.Core.Run;

public sealed partial class RunState : ICanonical
{
    private const int DefaultPlayerCapacity = 1;

    private RunState(List<PlayerState> players, RunRngSet rng, UnlockProfile unlocks, int ascension, List<ModifierModel> modifiers, ChoiceRuntime choice, FidelityLog fidelity)
    {
        Players = players;
        Rng = rng;
        Unlocks = unlocks;
        Ascension = ascension;
        Modifiers = modifiers;
        Choice = choice;
        Fidelity = fidelity;
    }

    public List<PlayerState> Players { get; }

    public CombatState? Combat { get; set; }

    public RunRngSet Rng { get; }

    public UnlockProfile Unlocks { get; }

    public int Ascension { get; }

    public List<ModifierModel> Modifiers { get; }

    public ChoiceRuntime Choice { get; }

    public FidelityLog Fidelity { get; }

    public static RunState CreateStandalone(PlayerState player, RunRngSet rng, int ascension, UnlockProfile? unlocks = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        var players = new List<PlayerState>(DefaultPlayerCapacity) { player };
        return new RunState(players, rng, unlocks ?? UnlockProfile.All, ascension, new List<ModifierModel>(), new ChoiceRuntime(), new FidelityLog());
    }

    public RunState DeepClone()
    {
        var clone = CloneWithoutCombat();
        var combat = Combat;
        if (combat != null)
        {
            clone.Combat = combat.CloneInto(clone, this);
        }
        return clone;
    }

    internal RunState CloneWithoutCombat()
    {
        var sourcePlayers = Players;
        var players = new List<PlayerState>(Math.Max(sourcePlayers.Count, DefaultPlayerCapacity));
        for (int i = 0; i < sourcePlayers.Count; i++)
        {
            players.Add(sourcePlayers[i].DeepClone());
        }
        var sourceModifiers = Modifiers;
        var modifiers = new List<ModifierModel>(sourceModifiers.Count);
        for (int i = 0; i < sourceModifiers.Count; i++)
        {
            modifiers.Add(sourceModifiers[i].DeepClone());
        }
        var rng = Rng is null ? null! : Rng.DeepClone();
        return new RunState(players, rng, Unlocks, Ascension, modifiers, Choice.DeepClone(), Fidelity.DeepClone());
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        WriteRunFields(w);
        var combat = Combat;
        w.WriteBool(combat != null);
        combat?.WriteCombatFields(w);
    }

    internal void WriteRunFields(CanonicalWriter w)
    {
        w.WriteTag(RunCanonicalTags.RunState);
        w.WriteInt32(Ascension);
        Unlocks.WriteCanonical(w);
        var players = Players;
        w.WriteTag(RunCanonicalTags.PlayerList);
        w.WriteCount(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            players[i].WriteCanonical(w);
        }
        var rng = Rng;
        w.WriteBool(rng is not null);
        rng?.WriteCanonical(w);
        var modifiers = Modifiers;
        w.WriteTag(RunCanonicalTags.ModifierList);
        w.WriteCount(modifiers.Count);
        for (int i = 0; i < modifiers.Count; i++)
        {
            modifiers[i].WriteCanonical(w);
        }
        Choice.WriteCanonical(w);
        Fidelity.WriteCanonical(w);
    }
}
