namespace Sts2Sim.Core.Cards;

public sealed class Jackpot : CardDef
{
    public const string CardId = "JACKPOT";
    public const int Cost = 3;
    public const decimal BaseDamage = 25m;
    public const int BaseCards = 3;
    public const decimal DamageUpgrade = 5m;

    public Jackpot()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
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
        var jackpot = play.Card;
        DamageCmd.Attack(cs, jackpot.Vars.Damage.BaseValue).FromCard(jackpot, play).Targeting(target).Execute();
        var owner = jackpot.Owner;
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.CanonicalCost == 0 && !c.CostsX);
        var forCombat = CardFactory.GetForCombat(cs, owner, options, jackpot.Vars.Cards.IntValue, rs.Rng.CombatCardGeneration);
        foreach (var current in forCombat)
        {
            if (jackpot.IsUpgraded)
            {
                CardCmd.Upgrade(cs, current);
            }
            CardPileCmd.AddGeneratedCardToCombat(cs, current, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
