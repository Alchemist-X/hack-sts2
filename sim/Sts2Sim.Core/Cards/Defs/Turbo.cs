namespace Sts2Sim.Core.Cards;

public sealed class Turbo : CardDef
{
    public const string CardId = "TURBO";
    public const int Cost = 0;
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;

    public Turbo()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var turbo = play.Card;
        var owner = turbo.Owner;
        PlayerCmd.GainEnergy(cs, turbo.Vars.Energy.IntValue, owner);
        var card = cs.CreateCard(Void.CardId, owner);
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
