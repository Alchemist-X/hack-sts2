namespace Sts2Sim.Core.Relics;

public sealed class RegalPillow : RelicModel
{
    public const string RelicId = "REGAL_PILLOW";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(15m)
    };

    public override decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount)
    {
        bool isOwner = ReferenceEquals(creature, Owner);
        bool isOwnersPet = creature is MonsterState pet && pet.PetOwnerId == Owner.CombatId;
        if (!isOwner && !isOwnersPet)
        {
            return amount;
        }
        return amount + DynamicVars.Heal.BaseValue;
    }

    public override void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        Status = RelicStatus.Normal;
    }

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        Status = room is RestSiteRoom ? RelicStatus.Active : RelicStatus.Normal;
    }
}
