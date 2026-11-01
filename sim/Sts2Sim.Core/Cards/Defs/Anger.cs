namespace Sts2Sim.Core.Cards;

public sealed class Anger : CardDef
{
    public const string CardId = "ANGER";
    public const int Cost = 0;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 2m;

    public Anger()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        var anger = play.Card;
        DamageCmd.Attack(cs, anger.Vars.Damage.BaseValue).FromCard(anger, play).Targeting(target).Execute();
        var card = anger.CreateClone(cs);
        card.ExhaustOnNextPlay = false;
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, anger.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
