namespace Sts2Sim.Core.Relics;

public sealed class CursedPearl : RelicModel
{
    public const string RelicId = "CURSED_PEARL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public const string GreedCardId = "GREED";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(333)
    };

    public override void AfterObtained(RunState rs)
    {
        CardPileCmd.AddCurseToDeck(rs, GreedCardId, Owner);
        PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
    }
}
