namespace Sts2Sim.Core.Potions;

public sealed class FoulPotion : PotionModel
{
    public const string PotionId = "FOUL_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Event;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AllEnemies;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Unpowered),
        new GoldVar(100)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        var cs = rs.Combat;
        if (cs != null && cs.IsInProgress)
        {
            var creatures = new List<CreatureState>();
            cs.GetCreatures(creatures);
            var targets = creatures.Where(c => !CombatStatus.IsPet(cs, c)).ToList();
            var damage = DynamicVars.Damage;
            CreatureCmd.Damage(cs, targets, damage.BaseValue, damage.Props, Owner, null, null);
            return;
        }
        throw new NotImplementedException("W3 run-layer: FoulPotion.OnUse out of combat (MerchantRoom GainGold / FakeMerchant.FoulPotionThrown)");
    }
}
