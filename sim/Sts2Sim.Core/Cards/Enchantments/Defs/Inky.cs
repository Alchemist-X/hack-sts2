namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Inky : EnchantmentModel
{
    public const string EnchantmentId = "INKY";
    public const decimal BaseWeak = 1m;

    public override string Id => EnchantmentId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        var card = Card!;
        IReadOnlyList<CreatureState> targets;
        if (card.TargetType != TargetType.AllEnemies)
        {
            targets = new[] { cardPlay!.Target! };
        }
        else
        {
            var hittable = new List<MonsterState>(4);
            cs.GetHittableEnemies(hittable);
            targets = hittable;
        }
        PowerCmd.Apply<WeakPower>(cs, targets, DynamicVars.Weak.BaseValue, card.Owner, card);
    }
}
