using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Expose : CardDef
{
    public const string CardId = "EXPOSE";
    public const int Cost = 0;
    public const string ArtifactPowerId = "ARTIFACT_POWER";
    public const string PowerKey = "Power";
    public const decimal BasePower = 2m;
    public const decimal PowerUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Expose()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        var owner = card.Owner;
        int amount = card.Vars[PowerKey].IntValue;
        CreatureCmd.LoseBlock(cs, target, target.Block, owner);
        if (target.HasPower(ArtifactPowerId))
        {
            PowerCmd.Remove(cs, target.GetPower(ArtifactPowerId));
        }
        PowerCmd.Apply<VulnerablePower>(cs, target, amount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerKey].UpgradeValueBy(PowerUpgrade);
    }
}
