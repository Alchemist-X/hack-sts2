using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SicEm : CardDef
{
    public const string CardId = "SIC_EM";
    public const int Cost = 1;
    public const decimal BaseOstyDamage = 5m;
    public const decimal BaseSicEm = 3m;
    public const decimal OstyDamageUpgrade = 1m;
    public const decimal SicEmUpgrade = 1m;
    public const string SicEmPowerId = "SIC_EM_POWER";
    public const string SicEmPowerVar = "SicEmPower";

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public SicEm()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new DynamicVar(SicEmPowerVar, BaseSicEm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var sicEm = play.Card;
        var owner = sicEm.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            DamageCmd.Attack(cs, sicEm.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, sicEm, play).Targeting(target).Execute();
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(SicEmPowerId), target, sicEm.Vars[SicEmPowerVar].BaseValue, owner, sicEm);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
        card.Vars[SicEmPowerVar].UpgradeValueBy(SicEmUpgrade);
    }
}
