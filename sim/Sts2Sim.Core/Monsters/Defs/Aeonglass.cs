namespace Sts2Sim.Core.Monsters;

public sealed class AeonglassExtraState : MonsterExtraState
{
    public int AdditionalStrength { get; set; }

    public int WitherUpgradeCount { get; set; }

    public override MonsterExtraState DeepClone() => new AeonglassExtraState
    {
        AdditionalStrength = AdditionalStrength,
        WitherUpgradeCount = WitherUpgradeCount
    };

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.AeonglassExtra);
        w.WriteInt32(AdditionalStrength);
        w.WriteInt32(WitherUpgradeCount);
    }
}

public sealed class Aeonglass : MonsterDef
{
    public const string MonsterIdEntry = "AEONGLASS";
    public const string EbbMoveId = "EBB_MOVE";
    public const string EyeLasersMoveId = "EYE_LASERS_MOVE";
    public const string IncreasingIntensityMoveId = "INCREASING_INTENSITY_MOVE";

    public const int InitialHpBase = 512;
    public const int InitialHpTough = 535;
    public const int EbbDamageBase = 22;
    public const int EbbDamageDeadly = 26;
    public const int EbbBlock = 33;
    public const int EyeLasersDamageBase = 11;
    public const int EyeLasersDamageDeadly = 12;
    public const int EyeLasersRepeat = 2;
    public const int IncreasingIntensityBaseStrengthBase = 3;
    public const int IncreasingIntensityBaseStrengthDeadly = 4;
    public const int WitherAmountBase = 1;
    public const int WitherAmountDeadly = 2;
    public const int WitheringPresenceAmount = 6;
    public const int SpawnArtifact = 3;

    public Aeonglass()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, InitialHpTough, InitialHpBase);

    public override int MaxInitialHp(CombatState cs) => MinInitialHp(cs);

    public static int EbbDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EbbDamageDeadly, EbbDamageBase);

    public static int EyeLasersDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, EyeLasersDamageDeadly, EyeLasersDamageBase);

    public static int IncreasingIntensityBaseStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, IncreasingIntensityBaseStrengthDeadly, IncreasingIntensityBaseStrengthBase);

    public static int WitherAmount(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WitherAmountDeadly, WitherAmountBase);

    public static int IncreasingIntensityTotalStrength(CombatState cs, MonsterState self) => IncreasingIntensityBaseStrength(cs) + GetExtra(self).AdditionalStrength;

    public static AeonglassExtraState GetExtra(MonsterState monster)
        => monster.Extra as AeonglassExtraState ?? throw new InvalidOperationException($"Monster {monster.MonsterId} has no Aeonglass state");

    public override MonsterExtraState? CreateExtraState() => new AeonglassExtraState();

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        var opponents = cs.GetOpponentsOf(self).ToArray();
        for (int i = 0; i < opponents.Length; i++)
        {
            var presence = PowerRegistry.Create(WitheringPresencePower.PowerId);
            presence.TargetId = opponents[i].CombatId;
            PowerCmd.Apply(cs, presence, self, WitheringPresenceAmount, self, null);
        }
        PowerCmd.Apply<ArtifactPower>(cs, self, SpawnArtifact, self, null);
    }

    public override void AfterCardGeneratedForCombat(CombatState cs, MonsterState self, CardInstance card, PlayerState? creator)
    {
        if (card.Def is not Wither)
        {
            return;
        }
        MatchWitherToUpgradeCount(self, card);
    }

    public static void MatchWitherToUpgradeCount(MonsterState self, CardInstance wither)
    {
        var def = (Wither)wither.Def;
        int count = GetExtra(self).WitherUpgradeCount;
        for (int i = 0; i < count; i++)
        {
            def.FakeUpgrade(wither);
        }
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var ebb = new MoveStateDef(EbbMoveId, EbbMove, new SingleAttackIntent(EbbDamage(cs)), Intents.Defend);
        var eyeLasers = new MoveStateDef(EyeLasersMoveId, EyeLasersMove, new MultiAttackIntent(EyeLasersDamage(cs), EyeLasersRepeat));
        var intensity = new MoveStateDef(IncreasingIntensityMoveId, IncreasingIntensityMove, new StatusIntent(WitherAmount(cs)), Intents.Buff);
        ebb.FollowUpState = eyeLasers;
        eyeLasers.FollowUpState = intensity;
        intensity.FollowUpState = ebb;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { ebb, eyeLasers, intensity }, ebb);
    }

    private static void EbbMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, EbbDamage(cs)).FromMonster(self).Execute();
        CreatureCmd.GainBlock(cs, self, EbbBlock, ValueProp.Move, null);
    }

    private static void EyeLasersMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, EyeLasersDamage(cs)).WithHitCount(EyeLasersRepeat).FromMonster(self).Execute();
    }

    private static void IncreasingIntensityMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is not PlayerState player)
            {
                continue;
            }
            var piles = player.AllPiles;
            for (int p = 0; p < piles.Count; p++)
            {
                var cards = piles[p].Cards;
                for (int c = 0; c < cards.Count; c++)
                {
                    if (cards[c].Def is Wither wither)
                    {
                        wither.FakeUpgrade(cards[c]);
                    }
                }
            }
        }
        var extra = GetExtra(self);
        extra.WitherUpgradeCount++;
        CardPileCmd.AddToCombatAndPreview(cs, targets, Wither.CardId, PileType.Discard, WitherAmount(cs), null);
        PowerCmd.Apply<StrengthPower>(cs, self, IncreasingIntensityTotalStrength(cs, self), self, null);
        extra.AdditionalStrength++;
    }
}
