namespace Sts2Sim.Core.Relics;

public sealed class Fiddle : RelicModel
{
    public const string RelicId = "FIDDLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        return count + DynamicVars.Cards.IntValue;
    }

    public override bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw)
    {
        if (fromHandDraw)
        {
            return true;
        }
        if (!ReferenceEquals(player, Owner))
        {
            return true;
        }
        if (player.Side != cs.CurrentSide)
        {
            return true;
        }
        return false;
    }
}
