namespace Sts2Sim.Core.Relics;

public sealed class LetterOpener : RelicModel
{
    public const string RelicId = "LETTER_OPENER";

    private bool _isActivating;
    private int _skillsPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return SkillsPlayedThisTurn % DynamicVars.Cards.IntValue;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new DamageVar(5m, ValueProp.Unpowered)
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

    private int SkillsPlayedThisTurn
    {
        get => _skillsPlayedThisTurn;
        set
        {
            _skillsPlayedThisTurn = value;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (IsActivating)
        {
            Status = RelicStatus.Normal;
        }
        else
        {
            int intValue = DynamicVars.Cards.IntValue;
            Status = SkillsPlayedThisTurn % intValue == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        SkillsPlayedThisTurn = 0;
        Status = RelicStatus.Normal;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner.TurnNumber == 1)
        {
            return;
        }
        SkillsPlayedThisTurn = 0;
        Status = RelicStatus.Normal;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || !cs.IsInProgress || card.Type != CardType.Skill)
        {
            return;
        }
        SkillsPlayedThisTurn++;
        int intValue = DynamicVars.Cards.IntValue;
        if (SkillsPlayedThisTurn % intValue == 0)
        {
            DoActivateVisuals();
            CreatureCmd.Damage(cs, HittableEnemies(cs), DynamicVars.Damage, Owner);
        }
    }

    private void DoActivateVisuals()
    {
        IsActivating = true;
        IsActivating = false;
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        Status = RelicStatus.Normal;
        IsActivating = false;
    }

    public int GetSkillsPlayedForTest() => SkillsPlayedThisTurn;

    private static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_isActivating);
        w.WriteInt32(_skillsPlayedThisTurn);
    }
}
