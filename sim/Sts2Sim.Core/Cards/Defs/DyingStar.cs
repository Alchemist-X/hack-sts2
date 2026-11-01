using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DyingStar : CardDef
{
    public const string CardId = "DYING_STAR";
    public const int Cost = 1;
    public const int StarCost = 3;
    public const string DyingStarPowerId = "DYING_STAR_POWER";
    public const string StrengthLossKey = "StrengthLoss";
    public const decimal BaseDamage = 9m;
    public const decimal BaseStrengthLoss = 9m;
    public const decimal DamageUpgrade = 2m;
    public const decimal StrengthLossUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public DyingStar()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(StrengthLossKey, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var dyingStar = play.Card;
        var owner = dyingStar.Owner;
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        DamageCmd.Attack(cs, dyingStar.Vars.Damage.BaseValue).FromCard(dyingStar, play).TargetingAllOpponents().Execute();
        for (int i = 0; i < enemies.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(DyingStarPowerId), enemies[i], dyingStar.Vars[StrengthLossKey].BaseValue, owner, dyingStar);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[StrengthLossKey].UpgradeValueBy(StrengthLossUpgrade);
    }
}
