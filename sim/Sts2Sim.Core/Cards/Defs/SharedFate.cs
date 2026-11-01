using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SharedFate : CardDef
{
    public const string CardId = "SHARED_FATE";
    public const int Cost = 0;
    public const string EnemyStrengthLossKey = "EnemyStrengthLoss";
    public const string PlayerStrengthLossKey = "PlayerStrengthLoss";
    public const decimal BaseEnemyStrengthLoss = 2m;
    public const decimal BasePlayerStrengthLoss = 2m;
    public const decimal EnemyStrengthLossUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public SharedFate()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(EnemyStrengthLossKey, BaseEnemyStrengthLoss),
        new DynamicVar(PlayerStrengthLossKey, BasePlayerStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply<StrengthPower>(cs, owner, -card.Vars[PlayerStrengthLossKey].BaseValue, owner, card);
        PowerCmd.Apply<StrengthPower>(cs, target, -card.Vars[EnemyStrengthLossKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[EnemyStrengthLossKey].UpgradeValueBy(EnemyStrengthLossUpgrade);
    }
}
