namespace Sts2Sim.Core.Relics;

public sealed class Orrery : RelicModel
{
    public const string RelicId = "ORRERY";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(5)
    };

    public override void AfterObtained(RunState rs)
    {
        if (!SmallCapsule.HasRunLayer(rs, this))
        {
            return;
        }
        var val = new List<Reward>();
        var options = new CardCreationOptions(new[] { CardPools.ForCharacter(Owner.CharacterId) }, CardCreationSource.Other, CardRarityOddsType.RegularEncounter);
        for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
        {
            val.Add(new CardReward(Owner, options, 3));
        }
        RewardsCmd.OfferCustom(rs, Owner, val);
    }
}
