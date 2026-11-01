namespace Sts2Sim.Core.Cards;

public sealed class Reave : CardDef
{
    public const string CardId = "REAVE";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 3m;

    public Reave()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var reave = play.Card;
        DamageCmd.Attack(cs, reave.Vars.Damage.BaseValue).FromCard(reave, play).Targeting(target).Execute();
        var souls = Soul.Create(cs, reave.Owner, reave.Vars.Cards.IntValue);
        if (reave.IsUpgraded)
        {
            foreach (var current in souls)
            {
                CardCmd.Upgrade(cs, current);
            }
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, souls, PileType.Draw, reave.Owner, CardPilePosition.Random);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
