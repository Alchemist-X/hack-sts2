namespace Sts2Sim.Core.Cards;

public sealed class Caltrops : CardDef
{
    public const string CardId = "CALTROPS";
    public const int Cost = 1;
    public const decimal BaseThorns = 3m;
    public const decimal ThornsUpgrade = 2m;
    public const string ThornsVarName = "ThornsPower";

    public Caltrops()
        : base(CardId, Cost, CardType.Power, CardRarity.Event, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ThornsPower>(BaseThorns)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply<ThornsPower>(cs, owner, card.Vars[ThornsVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ThornsVarName].UpgradeValueBy(ThornsUpgrade);
    }
}
