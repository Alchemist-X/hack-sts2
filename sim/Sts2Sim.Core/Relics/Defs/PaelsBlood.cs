namespace Sts2Sim.Core.Relics;

public sealed class PaelsBlood : RelicModel
{
    public const string RelicId = "PAELS_BLOOD";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(1)
    };

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        return count + DynamicVars.Cards.IntValue;
    }
}
