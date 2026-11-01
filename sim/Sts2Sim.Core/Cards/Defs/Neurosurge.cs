namespace Sts2Sim.Core.Cards;

public sealed class Neurosurge : CardDef
{
    public const string CardId = "NEUROSURGE";
    public const int Cost = 0;
    public const string NeurosurgePowerId = "NEUROSURGE_POWER";
    public const string NeurosurgeVarName = "NeurosurgePower";
    public const decimal BaseNeurosurge = 3m;
    public const int BaseEnergy = 3;
    public const int BaseCards = 2;
    public const decimal EnergyUpgrade = 1m;

    public Neurosurge()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(NeurosurgeVarName, BaseNeurosurge),
        new EnergyVar(BaseEnergy),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PlayerCmd.GainEnergy(cs, card.Vars.Energy.BaseValue, owner);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        PowerCmd.Apply(cs, PowerRegistry.Create(NeurosurgePowerId), owner, card.Vars[NeurosurgeVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
