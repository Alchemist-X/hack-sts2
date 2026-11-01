namespace Sts2Sim.Core.Run;

public abstract class AbstractOdds
{
    protected AbstractOdds(float initialValue, Rng rng)
    {
        CurrentValue = initialValue;
        Rng = rng;
    }

    public float CurrentValue { get; protected set; }

    protected Rng Rng { get; }

    public void OverrideCurrentValue(float newValue) => CurrentValue = newValue;
}

public sealed class CardRarityOdds : AbstractOdds
{
    public CardRarityOdds(float initialValue, Rng rng, int ascension) : base(initialValue, rng)
    {
        Ascension = ascension;
    }

    public int Ascension { get; }

    public CardRarity Roll(CardRarityOddsType type) => throw new NotImplementedException("W2 R3: CardRarityOdds.Roll");

    public CardRarity RollWithoutChangingFutureOdds(CardRarityOddsType oddsType) => throw new NotImplementedException("W2 R3: CardRarityOdds.RollWithoutChangingFutureOdds");

    public CardRarity RollWithBaseOdds(CardRarityOddsType type) => throw new NotImplementedException("W2 R3: CardRarityOdds.RollWithBaseOdds");

    public CardRarityOdds CloneFor(Rng rng) => throw new NotImplementedException("W2 R3: CardRarityOdds.CloneFor");
}

public sealed class PotionRewardOdds : AbstractOdds
{
    public PotionRewardOdds(float initialValue, Rng rng) : base(initialValue, rng)
    {
    }

    public bool Roll(RunState rs, PlayerState player, RoomType roomType) => throw new NotImplementedException("W2 R3: PotionRewardOdds.Roll");

    public PotionRewardOdds CloneFor(Rng rng) => throw new NotImplementedException("W2 R3: PotionRewardOdds.CloneFor");
}

public sealed class UnknownMapPointOdds : AbstractOdds
{
    public UnknownMapPointOdds(Rng rng) : base(0f, rng)
    {
    }

    public RoomType Roll(IEnumerable<RoomType> blacklist, RunState runState) => throw new NotImplementedException("W2 R3: UnknownMapPointOdds.Roll");

    public void SetBaseOdds(RoomType roomType, float baseOdds) => throw new NotImplementedException("W2 R3: UnknownMapPointOdds.SetBaseOdds");

    public void ResetToBase() => throw new NotImplementedException("W2 R3: UnknownMapPointOdds.ResetToBase");

    public UnknownMapPointOdds CloneFor(Rng rng) => throw new NotImplementedException("W2 R3: UnknownMapPointOdds.CloneFor");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: UnknownMapPointOdds.WriteCanonical");
}

public sealed class RunOddsSet
{
    public RunOddsSet(Rng unknownMapPointRng) => throw new NotImplementedException("W2 R3: RunOddsSet..ctor");

    public UnknownMapPointOdds UnknownMapPoint => throw new NotImplementedException("W2 R3: RunOddsSet.UnknownMapPoint");

    public RunOddsSet CloneFor(RunRngSet rng) => throw new NotImplementedException("W2 R3: RunOddsSet.CloneFor");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: RunOddsSet.WriteCanonical");
}

public sealed class PlayerOddsSet
{
    public PlayerOddsSet(PlayerRngSet rng, int ascension) => throw new NotImplementedException("W2 R3: PlayerOddsSet..ctor");

    public CardRarityOdds CardRarity => throw new NotImplementedException("W2 R3: PlayerOddsSet.CardRarity");

    public PotionRewardOdds PotionReward => throw new NotImplementedException("W2 R3: PlayerOddsSet.PotionReward");

    public PlayerOddsSet CloneFor(PlayerRngSet rng) => throw new NotImplementedException("W2 R3: PlayerOddsSet.CloneFor");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: PlayerOddsSet.WriteCanonical");
}
