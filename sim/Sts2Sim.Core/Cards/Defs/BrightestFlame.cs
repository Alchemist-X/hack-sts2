namespace Sts2Sim.Core.Cards;

public sealed class BrightestFlame : CardDef
{
    public const string CardId = "BRIGHTEST_FLAME";
    public const int Cost = 0;
    public const decimal BaseMaxHp = 2m;
    public const int BaseEnergy = 2;
    public const int BaseCards = 2;
    public const decimal EnergyUpgrade = 1m;
    public const decimal CardsUpgrade = 1m;

    public BrightestFlame()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(BaseMaxHp),
        new EnergyVar(BaseEnergy),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PlayerCmd.GainEnergy(cs, card.Vars.Energy.IntValue, owner);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        CreatureCmd.LoseMaxHp(cs, owner, card.Vars.MaxHp.BaseValue, isFromCard: true);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
