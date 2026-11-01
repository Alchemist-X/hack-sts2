namespace Sts2Sim.Core.Powers;

public sealed class CurlUpPower : PowerModel
{
    public const string PowerId = "CURL_UP_POWER";

    public const string LouseProgenitorId = "LOUSE_PROGENITOR";

    public const int NoCard = -1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public int PlayedCardInstanceId { get; private set; } = NoCard;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        if (!props.IsPoweredAttack())
        {
            return;
        }
        if (cardSource == null)
        {
            return;
        }
        if (PlayedCardInstanceId != NoCard && cardSource.InstanceId != PlayedCardInstanceId)
        {
            return;
        }
        PlayedCardInstanceId = cardSource.InstanceId;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cardPlay.Card.InstanceId != PlayedCardInstanceId)
        {
            return;
        }
        PlayedCardInstanceId = NoCard;
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        if (MonsterPowerCoupling.Is(Owner, LouseProgenitorId, out var louse))
        {
            MonsterPowerCoupling.SetFlag(louse, "Curled", true, PowerId);
        }
        PowerCmd.Remove(cs, this);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(PlayedCardInstanceId);
    }
}
