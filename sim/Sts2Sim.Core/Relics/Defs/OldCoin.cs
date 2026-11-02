namespace Sts2Sim.Core.Relics;

public sealed class OldCoin : RelicModel
{
    public const string RelicId = "OLD_COIN";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowedInShops => false;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new GoldVar(300)
    };

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs);

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
    }
}
