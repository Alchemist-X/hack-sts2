namespace Sts2Sim.Core.Relics;

public sealed class SparklingRouge : RelicModel
{
    public const string RelicId = "SPARKLING_ROUGE";

    public const int TriggerTurn = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m),
        new PowerVar<DexterityPower>(1m)
    };

    public override void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner) || Owner.TurnNumber != TriggerTurn)
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars.Strength.BaseValue, Owner, null);
        PowerCmd.Apply<DexterityPower>(cs, Owner, DynamicVars.Dexterity.BaseValue, Owner, null);
    }
}
