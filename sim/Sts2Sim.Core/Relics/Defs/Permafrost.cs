namespace Sts2Sim.Core.Relics;

public sealed class Permafrost : RelicModel
{
    public const string RelicId = "PERMAFROST";

    private bool _activatedThisCombat;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    private bool ActivatedThisCombat
    {
        get => _activatedThisCombat;
        set => _activatedThisCombat = value;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(7m, ValueProp.Unpowered)
    };

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not CombatRoom)
        {
            return;
        }
        ActivatedThisCombat = false;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (cs.IsInProgress && ReferenceEquals(cardPlay.Card.Owner, Owner) && cardPlay.Card.Type == CardType.Power && !ActivatedThisCombat)
        {
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
            ActivatedThisCombat = true;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_activatedThisCombat);
    }
}
