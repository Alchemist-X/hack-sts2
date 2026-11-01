namespace Sts2Sim.Core.Relics;

public sealed class LeafyPoultice : RelicModel
{
    public const string RelicId = "LEAFY_POULTICE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public const string StandaloneSite = "LeafyPoultice.AfterObtained: no PlayerRng (standalone combat); transforms skipped";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(12m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.LoseMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue, false);
        var basics = Owner.Deck.Where(c => c.Rarity == CardRarity.Basic).ToList();
        var strike = basics.FirstOrDefault(c => c.HasTag(CardTag.Strike));
        var defend = basics.FirstOrDefault(c => c.HasTag(CardTag.Defend));
        var transformations = new List<CardTransformation>();
        if (strike != null)
        {
            transformations.Add(new CardTransformation(rs.Combat, strike));
        }
        if (defend != null)
        {
            transformations.Add(new CardTransformation(rs.Combat, defend));
        }
        if (transformations.Count == 0)
        {
            return;
        }
        if (Owner.PlayerRng == null)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, StandaloneSite);
            return;
        }
        CardCmd.Transform(rs, transformations, Owner.PlayerRng.Transformations);
    }
}
