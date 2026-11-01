namespace Sts2Sim.Core.Relics;

public sealed class Brimstone : RelicModel
{
    public const string RelicId = "BRIMSTONE";

    public const string SelfStrengthKey = "SelfStrength";

    public const string EnemyStrengthKey = "EnemyStrength";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(SelfStrengthKey, 2m),
        new PowerVar<StrengthPower>(EnemyStrengthKey, 1m)
    };

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        using var context = cs.Choice.PushContext(ChoiceContextKind.Throwing);
        PowerCmd.Apply<StrengthPower>(cs, Owner, DynamicVars[SelfStrengthKey].BaseValue, Owner, null);
        var opponents = cs.GetOpponentsOf(Owner);
        var targets = new List<CreatureState>(opponents.Count);
        for (int i = 0; i < opponents.Count; i++)
        {
            if (opponents[i].IsAlive)
            {
                targets.Add(opponents[i]);
            }
        }
        PowerCmd.Apply<StrengthPower>(cs, targets, DynamicVars[EnemyStrengthKey].BaseValue, null, null);
    }
}
