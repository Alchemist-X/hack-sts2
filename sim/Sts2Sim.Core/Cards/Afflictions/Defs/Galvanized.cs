namespace Sts2Sim.Core.Cards.Afflictions;

public sealed class Galvanized : AfflictionModel
{
    public const string AfflictionId = "GALVANIZED";

    public override string Id => AfflictionId;

    public override bool IsStackable => true;
}
