namespace Sts2Sim.Core.Cards;

public sealed class EchoingSlash : CardDef
{
    public const string CardId = "ECHOING_SLASH";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const decimal DamageUpgrade = 3m;
    public const decimal ContextDamagePerHit = 0m;

    public EchoingSlash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var echoingSlash = play.Card;
        var owner = echoingSlash.Owner;
        var attackContext = new AttackCommand(cs, ContextDamagePerHit).FromCard(play.Card, play).TargetingAllOpponents();
        Hook.BeforeAttack(cs, attackContext);
        int attackCount = 1;
        var hittable = new List<MonsterState>(cs.Monsters.Count);
        while (attackCount > 0)
        {
            attackCount--;
            cs.GetHittableEnemies(hittable);
            var enumerable = CreatureCmd.Damage(cs, new List<CreatureState>(hittable), echoingSlash.Vars.Damage, owner, echoingSlash, play);
            attackContext.IncrementHitsInternal();
            attackContext.AddResultsInternal(enumerable);
            for (int i = 0; i < enumerable.Count; i++)
            {
                if (enumerable[i].WasTargetKilled)
                {
                    attackCount++;
                }
            }
        }
        Hook.AfterAttack(cs, attackContext);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
