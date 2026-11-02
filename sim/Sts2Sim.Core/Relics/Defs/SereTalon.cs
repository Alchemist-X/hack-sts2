namespace Sts2Sim.Core.Relics;

public sealed class SereTalon : RelicModel
{
    public const string RelicId = "SERE_TALON";

    public const string WishCardId = "WISH";

    public const int MaxHpLoss = 9;

    private const string WishesKey = "Wishes";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(9m),
        new DynamicVar(WishesKey, 3m)
    };

    public override void AfterObtained(RunState rs)
    {
        CreatureCmd.LoseMaxHp(rs, Owner, DynamicVars.HpLoss.BaseValue, isFromCard: false);
        for (int i = 0; i < DynamicVars[WishesKey].IntValue; i++)
        {
            var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(WishCardId), Owner);
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
    }
}
