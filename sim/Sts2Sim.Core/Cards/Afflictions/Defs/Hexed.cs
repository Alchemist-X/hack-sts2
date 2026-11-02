namespace Sts2Sim.Core.Cards.Afflictions;

public sealed class Hexed : AfflictionModel
{
    public const string AfflictionId = "HEXED";
    public const string HexPowerId = "HEX_POWER";

    public override string Id => AfflictionId;

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        var afflicted = Card;
        if (!ReferenceEquals(card, afflicted))
        {
            return;
        }
        if (card.Owner.HasPower(HexPowerId))
        {
            return;
        }
        CardCmd.ClearAffliction(afflicted);
    }
}
