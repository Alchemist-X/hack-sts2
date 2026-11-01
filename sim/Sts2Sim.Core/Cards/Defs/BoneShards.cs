using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BoneShards : CardDef
{
    public const string CardId = "BONE_SHARDS";
    public const int Cost = 1;
    public const decimal BaseOstyDamage = 9m;
    public const decimal BaseBlock = 9m;
    public const decimal OstyDamageUpgrade = 3m;
    public const decimal BlockUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public BoneShards()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var boneShards = play.Card;
        var owner = boneShards.Owner;
        if (Osty.CheckMissingWithAnim(cs, owner))
        {
            return;
        }
        DamageCmd.Attack(cs, boneShards.Vars.OstyDamage.BaseValue).FromOsty(PlayerPets.GetOsty(cs, owner)!, boneShards, play).TargetingAllOpponents().Execute();
        CreatureCmd.GainBlock(cs, owner, boneShards.Vars.Block, play);
        if (PlayerPets.IsOstyAlive(cs, owner))
        {
            CreatureCmd.Kill(cs, PlayerPets.GetOsty(cs, owner)!);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
