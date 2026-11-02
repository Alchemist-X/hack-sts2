namespace Sts2Sim.Core.Relics;

public sealed class TuningFork : RelicModel
{
    public const string RelicId = "TUNING_FORK";

    private bool _isActivating;
    private int _skillsPlayed;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool ShowCounter => true;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return SkillsPlayed;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(10),
        new BlockVar(7m, ValueProp.Unpowered)
    };

    private bool IsActivating
    {
        get => _isActivating;
        set
        {
            _isActivating = value;
            UpdateDisplay();
        }
    }

    public int SkillsPlayed
    {
        get => _skillsPlayed;
        set
        {
            if (_skillsPlayed != value)
            {
                _skillsPlayed = value;
                UpdateDisplay();
            }
        }
    }

    private int SkillsThreshold => DynamicVars.Cards.IntValue;

    private void UpdateDisplay()
    {
        if (IsActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            Status = SkillsPlayed == SkillsThreshold - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public void NotifySkillPlayed()
    {
        SkillsPlayed++;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner) || card.Type != CardType.Skill)
        {
            return;
        }
        SkillsPlayed++;
        if (SkillsPlayed >= SkillsThreshold)
        {
            DoActivateVisuals();
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
            SkillsPlayed -= SkillsThreshold;
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_skillsPlayed);
    }
}
