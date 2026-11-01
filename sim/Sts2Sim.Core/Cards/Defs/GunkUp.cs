namespace Sts2Sim.Core.Cards;

public sealed class GunkUp : CardDef
{
    public const string CardId = "GUNK_UP";
    public const int Cost = 1;
    public const decimal BaseDamage = 4m;
    public const int BaseRepeat = 3;
    public const decimal DamageUpgrade = 1m;

    public GunkUp()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var gunkUp = play.Card;
        var owner = gunkUp.Owner;
        DamageCmd.Attack(cs, gunkUp.Vars.Damage.BaseValue).WithHitCount(gunkUp.Vars.Repeat.IntValue).FromCard(gunkUp, play).Targeting(target).Execute();
        var card = cs.CreateCard(Slimed.CardId, owner);
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
