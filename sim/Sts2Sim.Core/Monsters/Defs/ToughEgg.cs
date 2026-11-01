namespace Sts2Sim.Core.Monsters;

public sealed class ToughEggExtraState : MonsterExtraState
{
    public bool IsHatched { get; set; }

    public override MonsterExtraState DeepClone() => new ToughEggExtraState
    {
        IsHatched = IsHatched
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.ToughEggExtra);
        w.WriteBool(IsHatched);
    }
}

public sealed class ToughEgg : MonsterDef
{
    public const string MonsterIdEntry = "TOUGH_EGG";
    public const string HatchMoveId = "HATCH_MOVE";
    public const string NibbleMoveId = "NIBBLE_MOVE";

    public const int MinInitialHpBase = 14;
    public const int MinInitialHpTough = 15;
    public const int MaxInitialHpBase = 18;
    public const int MaxInitialHpTough = 19;
    public const int HatchlingMinHpBase = 19;
    public const int HatchlingMinHpTough = 20;
    public const int HatchlingMaxHpBase = 22;
    public const int HatchlingMaxHpTough = 23;
    public const int NibbleDamageBase = 4;
    public const int NibbleDamageDeadly = 5;

    public ToughEgg()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int HatchlingMinHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HatchlingMinHpTough, HatchlingMinHpBase);

    public static int HatchlingMaxHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HatchlingMaxHpTough, HatchlingMaxHpBase);

    public static int NibbleDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, NibbleDamageDeadly, NibbleDamageBase);

    public static ToughEggExtraState GetExtra(MonsterState monster)
        => monster.Extra as ToughEggExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no ToughEgg state");

    public override MonsterExtraState? CreateExtraState() => new ToughEggExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        if (!GetExtra(self).IsHatched)
        {
            int amount = cs.CurrentSide != CombatSide.Enemy ? 1 : 2;
            PowerCmd.Apply(cs, HatchPower.PowerId, self, amount, self, null);
            return;
        }
        Hatch(cs, self);
        if (MonsterLifecycle.HasMoveStateMachine(self))
        {
            self.Ai.ForceCurrentState(NibbleMoveId);
        }
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var hatch = new MoveStateDef(HatchMoveId, HatchMove, Intents.Summon);
        var nibble = new MoveStateDef(NibbleMoveId, NibbleMove, new SingleAttackIntent(NibbleDamage(cs)));
        hatch.FollowUpState = nibble;
        nibble.FollowUpState = nibble;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { hatch, nibble }, hatch);
    }

    private static void HatchMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).IsHatched = true;
        PowerCmd.Remove<HatchPower>(cs, self);
        var powers = new List<PowerModel>();
        for (int i = 0; i < self.Powers.Count; i++)
        {
            if (self.Powers[i] is not MinionPower)
            {
                powers.Add(self.Powers[i]);
            }
        }
        for (int i = 0; i < powers.Count; i++)
        {
            PowerCmd.Remove(cs, powers[i]);
        }
        Hatch(cs, self);
    }

    private static void Hatch(CombatState cs, MonsterState self)
    {
        decimal amount = cs.Rng.Niche.NextInt(HatchlingMinHp(cs), HatchlingMaxHp(cs) + 1);
        CreatureCmd.SetMaxAndCurrentHp(cs, self, amount);
    }

    private static void NibbleMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, NibbleDamage(cs)).FromMonster(self).Execute();
    }
}
