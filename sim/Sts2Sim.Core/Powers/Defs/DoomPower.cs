namespace Sts2Sim.Core.Powers;

public sealed class DoomPower : PowerModel
{
    public const string PowerId = "DOOM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public static void DoomKill(CombatState cs, IReadOnlyList<CreatureState> creatures)
    {
        if (creatures.Count == 0)
        {
            return;
        }
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureCmd.Kill(cs, creatures[i]);
        }
        Hook.AfterDiedToDoom(cs, creatures);
    }

    public static IReadOnlyList<CreatureState> GetDoomedCreatures(IReadOnlyList<CreatureState> creatures)
    {
        var doomed = new List<CreatureState>();
        for (int i = 0; i < creatures.Count; i++)
        {
            if (creatures[i].GetPower<DoomPower>()?.IsOwnerDoomed() ?? false)
            {
                doomed.Add(creatures[i]);
            }
        }
        return doomed;
    }

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != CombatSide.Player && ShouldDoomTrigger(cs, participants))
        {
            DoomKill(cs, GetDoomedCreatures(cs, side));
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (side != CombatSide.Enemy && ShouldDoomTrigger(cs, participants))
        {
            DoomKill(cs, GetDoomedCreatures(cs, side));
        }
    }

    private bool ShouldDoomTrigger(CombatState cs, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return false;
        }
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return false;
        }
        if (Owner.IsDead)
        {
            return false;
        }
        if (!IsOwnerDoomed())
        {
            return false;
        }
        var doomedCreatures = GetDoomedCreatures(cs, Owner.Side);
        if (!ReferenceEquals(doomedCreatures[0], Owner))
        {
            return false;
        }
        return true;
    }

    private static IReadOnlyList<CreatureState> GetDoomedCreatures(CombatState cs, CombatSide side)
    {
        var onSide = new List<CreatureState>();
        CombatStatus.GetCreaturesOnSide(cs, side, onSide);
        return GetDoomedCreatures(onSide);
    }

    private bool IsOwnerDoomed() => Owner.CurrentHp <= Amount;
}
