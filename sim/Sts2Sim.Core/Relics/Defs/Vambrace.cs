namespace Sts2Sim.Core.Relics;

public sealed class Vambrace : RelicModel
{
    public const string RelicId = "VAMBRACE";

    public const int NoCard = -1;

    public const decimal BlockMultiplier = 2m;

    private int _triggeringCardId = NoCard;
    private bool _blockGainedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public int TriggeringCardId => _triggeringCardId;

    public bool BlockGainedThisCombat => _blockGainedThisCombat;

    public override void BeforeCombatStart(RunState rs)
    {
        _triggeringCardId = NoCard;
        _blockGainedThisCombat = false;
        Status = RelicStatus.Active;
    }

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsCardOrMonsterMove())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        if (_triggeringCardId != NoCard && _triggeringCardId != cardSource.InstanceId)
        {
            return 1m;
        }
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardSource), Owner))
        {
            return 1m;
        }
        if (_blockGainedThisCombat)
        {
            return 1m;
        }
        return BlockMultiplier;
    }

    public override void AfterModifyingBlockAmount(CombatState cs, decimal modifiedAmount, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (modifiedAmount <= 0m)
        {
            return;
        }
        if (cardSource == null)
        {
            return;
        }
        Status = RelicStatus.Normal;
        _triggeringCardId = cardSource.InstanceId;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            return;
        }
        if (cardPlay.Card.InstanceId != _triggeringCardId)
        {
            return;
        }
        if (_blockGainedThisCombat)
        {
            return;
        }
        _blockGainedThisCombat = true;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        _triggeringCardId = NoCard;
        _blockGainedThisCombat = false;
        Status = RelicStatus.Normal;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_triggeringCardId);
        w.WriteBool(_blockGainedThisCombat);
    }
}
