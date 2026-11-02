using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class HighFive : CardDef
{
    public const string CardId = "HIGH_FIVE";
    public const int Cost = 2;
    public const decimal BaseOstyDamage = 11m;
    public const decimal BaseVulnerable = 2m;
    public const decimal OstyDamageUpgrade = 2m;
    public const decimal VulnerableUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public HighFive()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override bool IsPlayable(CombatState cs, CardInstance card) => !PlayerPets.IsOstyMissing(cs, card.Owner);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var highFive = play.Card;
        var owner = highFive.Owner;
        if (Osty.CheckMissingWithAnim(cs, owner))
        {
            return;
        }
        DamageCmd.Attack(cs, highFive.Vars.OstyDamage.BaseValue).FromOsty(PlayerPets.GetOsty(cs, owner)!, highFive, play).TargetingAllOpponents().Execute();
        var hittable = new List<MonsterState>();
        cs.GetHittableEnemies(hittable);
        PowerCmd.Apply<VulnerablePower>(cs, hittable, highFive.Vars.Vulnerable.BaseValue, owner, highFive);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
