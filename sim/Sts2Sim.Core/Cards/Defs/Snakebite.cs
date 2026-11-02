using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Snakebite : CardDef
{
    public const string CardId = "SNAKEBITE";
    public const int Cost = 2;
    public const decimal BasePoison = 7m;
    public const decimal PoisonUpgrade = 3m;
    public const string PoisonPowerId = "POISON_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    public Snakebite()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, BasePoison)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        PowerCmd.Apply(cs, PoisonPowerId, target, card.Vars.Poison.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Poison.UpgradeValueBy(PoisonUpgrade);
    }
}
