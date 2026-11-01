namespace Sts2Sim.Core.State;

public interface IOwnedListener<in TOwner> where TOwner : class
{
    AbstractModel CloneFor(TOwner newOwner);
}
