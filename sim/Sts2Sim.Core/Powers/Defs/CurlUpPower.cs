namespace Sts2Sim.Core.Powers;

public sealed class CurlUpPower : PowerModel
{
    public const string PowerId = "CURL_UP_POWER";

    public const int NoCard = -1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public int PlayedCardId { get; private set; } = NoCard;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || !props.IsPoweredAttack() || cardSource == null)
        {
            return;
        }
        if (PlayedCardId != NoCard && cardSource.InstanceId != PlayedCardId)
        {
            return;
        }
        PlayedCardId = cardSource.InstanceId;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (PlayedCardId == NoCard || cardPlay.Card.InstanceId != PlayedCardId)
        {
            return;
        }
        PlayedCardId = NoCard;
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        PowerCmd.Remove(cs, this);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(PlayedCardId);
    }
}
