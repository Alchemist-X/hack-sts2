namespace Sts2Sim.Core.Cards;

public sealed class Ftl : CardDef
{
    public const string CardId = "FTL";
    public const int Cost = 0;
    public const decimal BaseDamage = 5m;
    public const string PlayMaxKey = "PlayMax";
    public const decimal BasePlayMax = 3m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 1m;
    public const decimal PlayMaxUpgrade = 1m;

    public Ftl()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new IntVar(PlayMaxKey, BasePlayMax),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var ftl = play.Card;
        DamageCmd.Attack(cs, ftl.Vars.Damage.BaseValue).FromCard(ftl, play).Targeting(target).Execute();
        if (CanDrawCard(cs, ftl))
        {
            CardPileCmd.Draw(cs, ftl.Vars.Cards.BaseValue, ftl.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[PlayMaxKey].UpgradeValueBy(PlayMaxUpgrade);
    }

    private static bool CanDrawCard(CombatState cs, CardInstance card)
    {
        int ownerId = card.Owner.CombatId;
        int num = cs.History.CountOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.PlayerId == ownerId);
        return num < card.Vars[PlayMaxKey].IntValue;
    }
}
