namespace Sts2Sim.Core.Powers;

public sealed class SwipePower : PowerModel
{
    public const string PowerId = "SWIPE_POWER";

    public const string ThievingHopperWeakEncounterId = "THIEVING_HOPPER_WEAK";

    public const string NoCombatRoomSite = "SWIPE_POWER.BeforeDeath: no CombatRoom on the room stack (standalone combat); the stolen card's SpecialCardReward is not added (Models.Powers/SwipePower.cs: (CombatRoom)runState.CurrentRoom)";

    private CardInstance? _stolenDeckVersion;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public string? StolenCardId { get; private set; }

    public CardInstance? StolenDeckVersion => _stolenDeckVersion;

    public void Steal(CombatState cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        TargetId = card.Owner.CombatId;
        StolenCardId = card.Id;
        var deckVersion = card.FindDeckVersion();
        if (deckVersion != null)
        {
            CardPileCmd.RemoveFromDeck(cs, deckVersion);
            _stolenDeckVersion = deckVersion.CloneFor(null);
        }
    }

    public override void BeforeDeath(RunState rs, CreatureState creature)
    {
        if (!ReferenceEquals(Owner, creature))
        {
            return;
        }
        var deckVersion = _stolenDeckVersion;
        if (deckVersion == null)
        {
            return;
        }
        var cs = rs.Combat!;
        var player = GetTarget(cs) as PlayerState ?? throw new InvalidOperationException("SWIPE_POWER.BeforeDeath: Target.Player is null");
        if (!deckVersion.HasOwner)
        {
            deckVersion.AttachTo(player);
        }
        deckVersion.HasBeenRemovedFromState = false;
        var specialCardReward = new SpecialCardReward(deckVersion, player);
        specialCardReward.SetCustomDescriptionEncounterSource(ThievingHopperWeakEncounterId);
        if (RoomStack.CurrentRoom(rs) is not CombatRoom combatRoom)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, NoCombatRoomSite);
            return;
        }
        combatRoom.AddExtraReward(specialCardReward);
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _stolenDeckVersion = ((SwipePower)source)._stolenDeckVersion?.CloneFor(null);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteString(StolenCardId);
        w.WriteBool(_stolenDeckVersion != null);
        _stolenDeckVersion?.WriteCanonical(w);
    }
}
