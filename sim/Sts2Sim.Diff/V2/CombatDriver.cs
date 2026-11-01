using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.V2;

public abstract class CombatDriver
{
    public abstract CombatState? State { get; }

    public abstract bool IsTerminal { get; }

    public abstract string OutcomeName { get; }

    public abstract int PlayerHp { get; }

    public abstract ChoicePrompt? PendingChoice { get; }

    public abstract IReadOnlyList<int> PendingPicks { get; }

    public abstract bool IsLegal(GameAction action, out string? error);

    public abstract void Step(GameAction action);

    public virtual RunSim? Run => null;
}

public sealed class StandaloneCombatDriver : CombatDriver
{
    public StandaloneCombatDriver(CombatSim sim)
    {
        Sim = sim;
    }

    public CombatSim Sim { get; }

    public override CombatState State => Sim.State;

    public override bool IsTerminal => Sim.IsTerminal;

    public override string OutcomeName => BundleRunner.OutcomeName(Sim.Outcome);

    public override int PlayerHp => Sim.State.Player.CurrentHp;

    public override ChoicePrompt? PendingChoice => Sim.PendingChoice;

    public override IReadOnlyList<int> PendingPicks => Sim.PendingPicks;

    public override bool IsLegal(GameAction action, out string? error)
    {
        if (Sim.PendingChoice is { } prompt)
        {
            return LegalActionGenerator.TryResolvePrompt(prompt, Sim.PendingPicks, action, out error);
        }
        return LegalActionGenerator.TryResolve(Sim.State, action, out _, out error);
    }

    public override void Step(GameAction action) => Sim.Step(action);
}

public sealed class RunCombatDriver : CombatDriver
{
    public RunCombatDriver(RunSim sim)
    {
        Sim = sim;
    }

    public RunSim Sim { get; }

    public override RunSim Run => Sim;

    public override CombatState? State => Sim.State.Combat;

    public override bool IsTerminal => !Sim.HasPendingChoice && Sim.State.Combat is not { IsInProgress: true } and not { IsStarting: true };

    public override string OutcomeName
    {
        get
        {
            var cs = Sim.State.Combat;
            if (cs != null && cs.Outcome != CombatOutcome.InProgress)
            {
                return BundleRunner.OutcomeName(cs.Outcome);
            }
            if (cs != null || Sim.HasPendingChoice)
            {
                return DiffBundle.OutcomeAbandoned;
            }
            return Sim.State.Players.Count > 0 && Sim.State.Players[0].IsDead ? DiffBundle.OutcomeLoss : DiffBundle.OutcomeWin;
        }
    }

    public override int PlayerHp => Sim.State.Players[0].CurrentHp;

    public override ChoicePrompt? PendingChoice => Sim.PendingChoice;

    public override IReadOnlyList<int> PendingPicks => Sim.PendingPicks;

    public override bool IsLegal(GameAction action, out string? error)
    {
        var ra = RunAction.FromCombat(action);
        if (Sim.IsLegal(ra))
        {
            error = null;
            return true;
        }
        error = Sim.PendingChoice is { } prompt
            ? (LegalActionGenerator.TryResolvePrompt(prompt, Sim.PendingPicks, action, out var pe) ? "rejected by RunSim" : pe)
            : Sim.State.Combat is { } cs && !LegalActionGenerator.TryResolve(cs, action, out _, out var ce) ? ce : $"RunSim rejects {ra} in phase {Sim.Phase}";
        return false;
    }

    public override void Step(GameAction action) => Sim.Step(RunAction.FromCombat(action));
}
