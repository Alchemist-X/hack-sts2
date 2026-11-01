namespace Sts2Sim.Core.Cards;

public sealed class Bloodletting : CardDef
{
    public const string CardId = "BLOODLETTING";
    public const int Cost = 0;
    public const decimal BaseHpLoss = 3m;
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;

    public Bloodletting()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.Damage(cs, owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
        PlayerCmd.GainEnergy(cs, card.Vars.Energy.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
