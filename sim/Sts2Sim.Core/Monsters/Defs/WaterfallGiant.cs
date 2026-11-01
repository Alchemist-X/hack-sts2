namespace Sts2Sim.Core.Monsters;

public sealed class WaterfallGiantExtraState : MonsterExtraState
{
    public int CurrentPressureGunDamage { get; set; }

    public int SteamEruptionDamage { get; set; }

    public bool IsAboutToBlow { get; set; }

    public override MonsterExtraState DeepClone() => new WaterfallGiantExtraState
    {
        CurrentPressureGunDamage = CurrentPressureGunDamage,
        SteamEruptionDamage = SteamEruptionDamage,
        IsAboutToBlow = IsAboutToBlow
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.WaterfallGiantExtra);
        w.WriteInt32(CurrentPressureGunDamage);
        w.WriteInt32(SteamEruptionDamage);
        w.WriteBool(IsAboutToBlow);
    }
}

public sealed class WaterfallGiant : MonsterDef
{
    public const string MonsterIdEntry = "WATERFALL_GIANT";
    public const string PressurizeMoveId = "PRESSURIZE_MOVE";
    public const string StompMoveId = "STOMP_MOVE";
    public const string RamMoveId = "RAM_MOVE";
    public const string SiphonMoveId = "SIPHON_MOVE";
    public const string PressureGunMoveId = "PRESSURE_GUN_MOVE";
    public const string PressureUpMoveId = "PRESSURE_UP_MOVE";
    public const string AboutToBlowMoveId = "ABOUT_TO_BLOW_MOVE";
    public const string ExplodeMoveId = "EXPLODE_MOVE";

    public const int HpBase = 240;
    public const int HpTough = 250;
    public const int SiphonHealBase = 10;
    public const int SiphonHealTough = 15;
    public const int PressurizeAmountBase = 15;
    public const int PressurizeAmountDeadly = 20;
    public const int StompDamageBase = 15;
    public const int StompDamageDeadly = 16;
    public const int RamDamageBase = 10;
    public const int RamDamageDeadly = 11;
    public const int PressureUpDamageBase = 13;
    public const int PressureUpDamageDeadly = 14;
    public const int BasePressureGunDamageBase = 20;
    public const int BasePressureGunDamageDeadly = 23;
    public const int PressureGunIncrease = 5;
    public const int SteamPerMove = 3;
    public const int StompWeak = 1;
    public const decimal AboutToBlowHp = 999999999m;
    public const int SinglePlayerCount = 1;

    public WaterfallGiant()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, HpTough, HpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int SiphonHeal(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, SiphonHealTough, SiphonHealBase);

    public static int PressurizeAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PressurizeAmountDeadly, PressurizeAmountBase);

    public static int StompDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, StompDamageDeadly, StompDamageBase);

    public static int RamDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, RamDamageDeadly, RamDamageBase);

    public static int PressureUpDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PressureUpDamageDeadly, PressureUpDamageBase);

    public static int BasePressureGunDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, BasePressureGunDamageDeadly, BasePressureGunDamageBase);

    public static WaterfallGiantExtraState GetExtra(MonsterState monster)
        => monster.Extra as WaterfallGiantExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no WaterfallGiant state");

    public override MonsterExtraState? CreateExtraState() => new WaterfallGiantExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        GetExtra(self).CurrentPressureGunDamage = BasePressureGunDamage(cs);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var pressurize = new MoveStateDef(PressurizeMoveId, PressurizeMove, Intents.Buff);
        var stomp = new MoveStateDef(StompMoveId, StompMove, new SingleAttackIntent(StompDamage(cs)), new DebuffIntent(), Intents.Buff);
        var ram = new MoveStateDef(RamMoveId, RamMove, new SingleAttackIntent(RamDamage(cs)), Intents.Buff);
        var siphon = new MoveStateDef(SiphonMoveId, SiphonMove, Intents.Heal, Intents.Buff);
        var pressureGun = new MoveStateDef(PressureGunMoveId, PressureGunMove, new SingleAttackIntent(static (_, m) => GetExtra(m).CurrentPressureGunDamage), Intents.Buff);
        var pressureUp = new MoveStateDef(PressureUpMoveId, PressureUpMove, new SingleAttackIntent(PressureUpDamage(cs)), Intents.Buff);
        var aboutToBlow = new MoveStateDef(AboutToBlowMoveId, AboutToBlowMove, Intents.Stun)
        {
            MustPerformOnceBeforeTransitioning = true
        };
        var explode = new MoveStateDef(ExplodeMoveId, ExplodeMove, new DeathBlowIntent(static (_, m) => GetExtra(m).SteamEruptionDamage));
        pressurize.FollowUpState = stomp;
        stomp.FollowUpState = ram;
        ram.FollowUpState = siphon;
        siphon.FollowUpState = pressureGun;
        pressureGun.FollowUpState = pressureUp;
        pressureUp.FollowUpState = stomp;
        aboutToBlow.FollowUpState = explode;
        explode.FollowUpState = explode;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { pressurize, stomp, ram, siphon, pressureGun, pressureUp, explode, aboutToBlow }, pressurize);
    }

    public static void TriggerAboutToBlowState(CombatState cs, MonsterState self)
    {
        GetExtra(self).IsAboutToBlow = true;
        CreatureCmd.SetMaxAndCurrentHp(cs, self, AboutToBlowHp);
        if (self.Ai.Machine.TryGet(AboutToBlowMoveId, out var state) && state is MoveStateDef aboutToBlow)
        {
            MonsterLifecycle.SetMoveImmediate(self, aboutToBlow, forceTransition: true);
        }
    }

    private static void PressurizeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        PowerCmd.Apply<SteamEruptionPower>(cs, self, PressurizeAmount(cs), self, null);
    }

    private static void StompMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, StompDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<WeakPower>(cs, targets, StompWeak, self, null);
        PowerCmd.Apply<SteamEruptionPower>(cs, self, SteamPerMove, self, null);
    }

    private static void RamMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, RamDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<SteamEruptionPower>(cs, self, SteamPerMove, self, null);
    }

    private static void SiphonMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.Heal(cs, self, SiphonHeal(cs) * SinglePlayerCount);
        PowerCmd.Apply<SteamEruptionPower>(cs, self, SteamPerMove, self, null);
    }

    private static void PressureGunMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        var extra = GetExtra(self);
        DamageCmd.Attack(cs, extra.CurrentPressureGunDamage).FromMonster(self).Execute();
        extra.CurrentPressureGunDamage += PressureGunIncrease;
        PowerCmd.Apply<SteamEruptionPower>(cs, self, SteamPerMove, self, null);
    }

    private static void PressureUpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PressureUpDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<SteamEruptionPower>(cs, self, SteamPerMove, self, null);
    }

    private static void AboutToBlowMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        GetExtra(self).SteamEruptionDamage = self.GetPowerAmount<SteamEruptionPower>();
        PowerCmd.Remove<SteamEruptionPower>(cs, self);
    }

    private static void ExplodeMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, GetExtra(self).SteamEruptionDamage).FromMonster(self).Execute();
        CreatureCmd.Kill(cs, self);
    }
}
