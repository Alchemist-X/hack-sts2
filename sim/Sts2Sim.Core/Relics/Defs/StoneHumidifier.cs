namespace Sts2Sim.Core.Relics;

public sealed class StoneHumidifier : RelicModel
{
    public const string RelicId = "STONE_HUMIDIFIER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(5m)
    };

    public override void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        decimal amount = DynamicVars.MaxHp.BaseValue;
        var cs = rs.Combat;
        if (cs != null)
        {
            CreatureCmd.GainMaxHp(cs, Owner, amount);
            return;
        }
        int oldMaxHp = Owner.MaxHp;
        CreatureInternals.SetMaxHpInternal(Owner, Owner.MaxHp + amount);
        CreatureInternals.HealInternal(Owner, Owner.MaxHp - oldMaxHp);
    }
}
