using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Shockwave : CardDef
{
    public const string CardId = "SHOCKWAVE";
    public const int Cost = 2;
    public const string PowerKey = "Power";
    public const decimal BasePower = 3m;
    public const decimal PowerUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Shockwave()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        int amount = card.Vars[PowerKey].IntValue;
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            PowerCmd.Apply<WeakPower>(cs, enemy, amount, owner, card);
            PowerCmd.Apply<VulnerablePower>(cs, enemy, amount, owner, card);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerKey].UpgradeValueBy(PowerUpgrade);
    }
}
