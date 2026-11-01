namespace Sts2Sim.Core.Relics;

public sealed class PaelsFlesh : RelicModel
{
    public const string RelicId = "PAELS_FLESH";

    public const int StartTurn = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override int DisplayAmount => HasOwner ? Owner.TurnNumber : 1;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(1)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        if (player.TurnNumber < StartTurn)
        {
            return amount;
        }
        return amount + DynamicVars.Energy.BaseValue;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner.TurnNumber < StartTurn)
        {
            return;
        }
        if (Status == RelicStatus.Active)
        {
            return;
        }
        Status = RelicStatus.Active;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
    }
}
