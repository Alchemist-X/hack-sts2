namespace Sts2Sim.Core.Cards.Afflictions;

public sealed class Tainted : AfflictionModel
{
    public const string AfflictionId = "TAINTED";

    public override string Id => AfflictionId;

    public override bool IsStackable => true;

    public override bool CanAfflictCardType(CardType cardType) => cardType == CardType.Skill;
}
