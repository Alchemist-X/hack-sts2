namespace Sts2Sim.Core.Cards;

public sealed class SweepingBeam : CardDef
{
    public const string CardId = "SWEEPING_BEAM";
    public const int Cost = 1;
    public const decimal BaseDamage = 6m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 3m;

    public SweepingBeam()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sweepingBeam = play.Card;
        DamageCmd.Attack(cs, sweepingBeam.Vars.Damage.BaseValue).FromCard(sweepingBeam, play).TargetingAllOpponents().Execute();
        CardPileCmd.Draw(cs, sweepingBeam.Vars.Cards.BaseValue, sweepingBeam.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
