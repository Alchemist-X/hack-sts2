namespace Sts2Sim.Core.Run;

public abstract partial class RestSiteOption
{
    protected RestSiteOption(PlayerState owner)
    {
        Owner = owner;
    }

    public PlayerState Owner { get; protected set; }

    public abstract string OptionId { get; }

    public virtual bool IsEnabled => true;

    public static List<RestSiteOption> Generate(RunState rs, PlayerState player) => throw new NotImplementedException("W2 R4: RestSiteOption.Generate");

    public virtual bool OnSelect(RunState rs) => throw new NotImplementedException("W2 R4: RestSiteOption.OnSelect");

    public virtual RestSiteOption CloneFor(PlayerState owner) => throw new NotImplementedException("W2 R4: RestSiteOption.CloneFor");
}
