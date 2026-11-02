namespace Sts2Sim.Core.Relics;

public sealed class BoneFlute : RelicModel
{
    public const string RelicId = "BONE_FLUTE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(2m, ValueProp.Unpowered)
    };

    public override void AfterAttack(CombatState cs, AttackCommand command)
    {
        if (command.Attacker is not MonsterState attacker || attacker.Def is not Osty)
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.PetOwnerOf(cs, attacker), Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }
}
