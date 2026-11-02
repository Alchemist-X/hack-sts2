namespace Sts2Sim.Core.Cards;

public sealed class Omnislice : CardDef
{
    public const string CardId = "OMNISLICE";
    public const int Cost = 0;
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 3m;

    public Omnislice()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var omnislice = play.Card;
        using var context = AttackContext.CreateContext(cs, play);
        var results = new List<DamageResult>(CreatureCmd.Damage(cs, target, omnislice.Vars.Damage.BaseValue, ValueProp.Move, omnislice, play));
        context.AddHit(results);
        var damageResult = results.Count > 0 ? results[0] : null;
        if (damageResult == null)
        {
            return;
        }
        var teammates = cs.GetTeammatesOf(damageResult.Receiver);
        var splashTargets = new List<CreatureState>(teammates.Count);
        for (int i = 0; i < teammates.Count; i++)
        {
            var e = teammates[i];
            if (!ReferenceEquals(e, target) && !splashTargets.Contains(e) && CombatStatus.IsHittable(cs, e))
            {
                splashTargets.Add(e);
            }
        }
        if (splashTargets.Count != 0)
        {
            var splash = CreatureCmd.Damage(cs, splashTargets, damageResult.TotalDamage + damageResult.OverkillDamage, ValueProp.Unpowered | ValueProp.Move, omnislice.Owner, omnislice, play);
            context.AddHit(splash);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
