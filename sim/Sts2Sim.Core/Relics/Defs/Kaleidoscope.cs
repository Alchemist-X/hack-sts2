namespace Sts2Sim.Core.Relics;

public sealed class Kaleidoscope : RelicModel
{
    public const string RelicId = "KALEIDOSCOPE";

    public const int PoolsPerReward = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

    public override bool IsAllowedAtNeow(RunState rs, PlayerState player)
    {
        if (base.IsAllowedAtNeow(rs, player))
        {
            return rs.Unlocks.CharacterCardPools.Count == CardPools.AllCharacterIds.Count;
        }
        return false;
    }

    public override void AfterObtained(RunState rs)
    {
        if (Owner.PlayerRng == null)
        {
            return;
        }
        var val = new List<Reward>();
        var rerollOptions = CardCreationOptions.ForNonCombatWithDefaultOdds(Array.Empty<CardPool>());
        var ownPool = CardPools.ForCharacter(Owner.CharacterId);
        for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
        {
            var val2 = new List<CardInstance>();
            var enumerable = rs.Unlocks.CharacterCardPools.Where(p => !ReferenceEquals(p, ownPool)).ToList()
                .StableShuffle(rs.Rng.Niche, ComparePools).Take(PoolsPerReward);
            foreach (var current in enumerable)
            {
                var options = new CardCreationOptions(new[] { current }, CardCreationSource.Other, CardRarityOddsType.RegularEncounter)
                    .WithFlags(CardCreationFlags.NoCardPoolModifications);
                val2.Add(CardFactory.CreateForReward(rs, Owner, 1, options).First().Card);
            }
            val.Add(new CardReward(val2, CardCreationSource.Other, Owner, rerollOptions));
        }
        RewardsCmd.OfferCustom(rs, Owner, val);
    }

    private static int ComparePools(CardPool a, CardPool b) => string.CompareOrdinal(a.Id, b.Id);
}
