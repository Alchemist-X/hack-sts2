namespace Sts2Sim.Core.Cards;

public sealed class CrashLanding : CardDef
{
    public const string CardId = "CRASH_LANDING";
    public const int Cost = 1;
    public const decimal BaseDamage = 21m;
    public const decimal DamageUpgrade = 5m;
    public const string DebrisId = "DEBRIS";

    public CrashLanding()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var crashLanding = play.Card;
        var owner = crashLanding.Owner;
        DamageCmd.Attack(cs, crashLanding.Vars.Damage.BaseValue).FromCard(crashLanding, play).TargetingAllOpponents().Execute();
        int num = CardPile.MaxCardsInHand - owner.Hand.Cards.Count;
        var debris = new List<CardInstance>(Math.Max(0, num));
        for (int i = 0; i < num; i++)
        {
            debris.Add(cs.CreateCard(DebrisId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, debris, PileType.Hand, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
