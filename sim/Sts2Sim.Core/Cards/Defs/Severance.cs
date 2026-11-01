namespace Sts2Sim.Core.Cards;

public sealed class Severance : CardDef
{
    public const string CardId = "SEVERANCE";
    public const int Cost = 2;
    public const decimal BaseDamage = 13m;
    public const decimal DamageUpgrade = 5m;
    public const int SoulCount = 3;

    public Severance()
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
        var severance = play.Card;
        DamageCmd.Attack(cs, severance.Vars.Damage.BaseValue).FromCard(severance, play).Targeting(target).Execute();
        var owner = severance.Owner;
        var souls = Soul.Create(cs, owner, SoulCount);
        CardPileCmd.AddGeneratedCardToCombat(cs, souls[0], PileType.Draw, owner, CardPilePosition.Random);
        CardPileCmd.AddGeneratedCardToCombat(cs, souls[1], PileType.Discard, owner);
        CardPileCmd.AddGeneratedCardToCombat(cs, souls[2], PileType.Hand, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
