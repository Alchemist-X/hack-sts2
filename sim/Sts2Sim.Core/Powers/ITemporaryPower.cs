namespace Sts2Sim.Core.Powers;

public interface ITemporaryPower
{
    string OriginModelId { get; }

    string InternallyAppliedPowerId { get; }
}
