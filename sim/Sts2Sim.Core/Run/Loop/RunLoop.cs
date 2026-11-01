namespace Sts2Sim.Core.Run;

public static class RunLoop
{
    public static RunState CreateForNewRun(RunConfig config) => throw new NotImplementedException("W2 R6: RunLoop.CreateForNewRun");

    public static void EnterAct(RunState rs, int actIndex) => throw new NotImplementedException("W2 R6: RunLoop.EnterAct");

    public static void EnterMapCoord(RunState rs, MapCoord coord) => throw new NotImplementedException("W2 R6: RunLoop.EnterMapCoord");

    public static void ProceedFromTerminalRewardsScreen(RunState rs) => throw new NotImplementedException("W2 R6: RunLoop.ProceedFromTerminalRewardsScreen");

    public static void MoveToNextAct(RunState rs) => throw new NotImplementedException("W2 R6: RunLoop.MoveToNextAct");
}
