namespace Sts2Sim.Core.Run;

public sealed class RestSiteRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.RestSite;

    public List<RestSiteOption> Options { get; } = new();

    public bool ProceedAllowed { get; private set; }

    public bool ChooseOption(RunState rs, int optionIndex) => throw new NotImplementedException("W2 R4: RestSiteRoom.ChooseOption");
}
