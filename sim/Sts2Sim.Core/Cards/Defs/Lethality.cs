using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Lethality : CardDef
{
    public const string CardId = "LETHALITY";
    public const int Cost = 1;
    public const string LethalityPowerKey = "LethalityPower";
    public const decimal BaseLethality = 50m;
    public const decimal LethalityUpgrade = 25m;
    public const string LethalityPowerId = "LETHALITY_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Lethality()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(LethalityPowerKey, BaseLethality)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var lethality = play.Card;
        var owner = lethality.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(LethalityPowerId), owner, lethality.Vars[LethalityPowerKey].BaseValue, owner, lethality);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[LethalityPowerKey].UpgradeValueBy(LethalityUpgrade);
    }
}
