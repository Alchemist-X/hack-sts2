using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Abrasive : CardDef
{
    public const string CardId = "ABRASIVE";
    public const int Cost = 3;
    public const decimal BaseThorns = 4m;
    public const decimal BaseDexterity = 1m;
    public const decimal ThornsUpgrade = 2m;
    public const string ThornsVarName = "ThornsPower";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public Abrasive()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ThornsPower>(BaseThorns),
        new PowerVar<DexterityPower>(BaseDexterity)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var abrasive = play.Card;
        var owner = abrasive.Owner;
        PowerCmd.Apply<DexterityPower>(cs, owner, abrasive.Vars.Dexterity.BaseValue, owner, abrasive);
        PowerCmd.Apply<ThornsPower>(cs, owner, abrasive.Vars[ThornsVarName].BaseValue, owner, abrasive);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[ThornsVarName].UpgradeValueBy(ThornsUpgrade);
    }
}
