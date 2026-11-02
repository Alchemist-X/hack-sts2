namespace Sts2Sim.Core.Cards;

public sealed class BulkUp : CardDef
{
    public const string CardId = "BULK_UP";
    public const int Cost = 2;
    public const string OrbSlotVar = "OrbSlots";
    public const decimal BaseOrbSlots = 1m;
    public const decimal BaseStrength = 2m;
    public const decimal BaseDexterity = 2m;
    public const decimal StrengthUpgrade = 1m;
    public const decimal DexterityUpgrade = 1m;

    public BulkUp()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(OrbSlotVar, BaseOrbSlots),
        new PowerVar<StrengthPower>(BaseStrength),
        new PowerVar<DexterityPower>(BaseDexterity)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bulkUp = play.Card;
        var owner = bulkUp.Owner;
        OrbCmd.RemoveSlots(cs, owner, bulkUp.Vars[OrbSlotVar].IntValue);
        PowerCmd.Apply<StrengthPower>(cs, owner, bulkUp.Vars.Strength.BaseValue, owner, bulkUp);
        PowerCmd.Apply<DexterityPower>(cs, owner, bulkUp.Vars.Dexterity.BaseValue, owner, bulkUp);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
        card.Vars.Dexterity.UpgradeValueBy(DexterityUpgrade);
    }
}
