namespace Sts2Sim.Core.Cards;

public sealed class Juggernaut : CardDef
{
    public const string CardId = "JUGGERNAUT";
    public const int Cost = 2;
    public const string JuggernautPowerId = "JUGGERNAUT_POWER";
    public const string JuggernautVarName = "JuggernautPower";
    public const decimal BaseJuggernaut = 6m;
    public const decimal JuggernautUpgrade = 2m;

    public Juggernaut()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(JuggernautVarName, BaseJuggernaut)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(JuggernautPowerId), owner, card.Vars[JuggernautVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[JuggernautVarName].UpgradeValueBy(JuggernautUpgrade);
    }
}
