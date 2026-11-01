namespace Sts2Sim.Core.Relics;

public sealed class Kusarigama : RelicModel
{
    public const string RelicId = "KUSARIGAMA";

    private bool _isActivating;
    private int _attacksPlayedThisTurn;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override int DisplayAmount
    {
        get
        {
            if (!IsActivating)
            {
                return AttacksPlayedThisTurn % DynamicVars.Cards.IntValue;
            }
            return DynamicVars.Cards.IntValue;
        }
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new DamageVar(6m, ValueProp.Unpowered)
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

    private int AttacksPlayedThisTurn
    {
        get => _attacksPlayedThisTurn;
        set
        {
            _attacksPlayedThisTurn = value;
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
            Status = AttacksPlayedThisTurn % intValue == intValue - 1 ? RelicStatus.Active : RelicStatus.Normal;
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        AttacksPlayedThisTurn = 0;
        Status = RelicStatus.Normal;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        AttacksPlayedThisTurn = 0;
        Status = RelicStatus.Normal;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner) || !cs.IsInProgress || card.Type != CardType.Attack)
        {
            return;
        }
        AttacksPlayedThisTurn++;
        int intValue = DynamicVars.Cards.IntValue;
        if (AttacksPlayedThisTurn % intValue == 0)
        {
            var creature = cs.Rng.CombatTargets.NextItem(HittableEnemies(cs));
            if (creature != null)
            {
                DoActivateVisuals();
                CreatureCmd.Damage(cs, creature, DynamicVars.Damage, Owner);
            }
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
        w.WriteInt32(_attacksPlayedThisTurn);
    }
}
