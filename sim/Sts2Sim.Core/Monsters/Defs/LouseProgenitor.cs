namespace Sts2Sim.Core.Monsters;

public sealed class LouseProgenitor : MonsterDef
{
    public const string MonsterIdEntry = "LOUSE_PROGENITOR";
    public const string WebCannonMoveId = "WEB_CANNON_MOVE";
    public const string PounceMoveId = "POUNCE_MOVE";
    public const string CurlAndGrowMoveId = "CURL_AND_GROW_MOVE";

    public const int MinInitialHpBase = 134;
    public const int MinInitialHpTough = 138;
    public const int MaxInitialHpBase = 136;
    public const int MaxInitialHpTough = 141;
    public const int WebDamageBase = 9;
    public const int WebDamageDeadly = 10;
    public const int PounceDamageBase = 14;
    public const int PounceDamageDeadly = 16;
    public const int CurlBlockBase = 14;
    public const int CurlBlockTough = 18;
    public const int GrowStrengthBase = 5;
    public const int GrowStrengthDeadly = 7;
    public const int WebFrail = 2;

    public LouseProgenitor()
        : base(MonsterIdEntry)
    {
    }

    public override int MinInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MinInitialHpTough, MinInitialHpBase);

    public override int MaxInitialHp(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, MaxInitialHpTough, MaxInitialHpBase);

    public static int WebDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, WebDamageDeadly, WebDamageBase);

    public static int PounceDamage(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, PounceDamageDeadly, PounceDamageBase);

    public static int CurlBlock(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.ToughEnemies, CurlBlockTough, CurlBlockBase);

    public static int GrowStrength(CombatState cs) => cs.GetValueIfAscension(AscensionLevel.DeadlyEnemies, GrowStrengthDeadly, GrowStrengthBase);

    public override void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
        PowerCmd.Apply<CurlUpPower>(cs, self, CurlBlock(cs), self, null);
    }

    public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    {
        var web = new MoveStateDef(WebCannonMoveId, WebMove, new SingleAttackIntent(WebDamage(cs)), new DebuffIntent());
        var pounce = new MoveStateDef(PounceMoveId, PounceMove, new SingleAttackIntent(PounceDamage(cs)));
        var curlAndGrow = new MoveStateDef(CurlAndGrowMoveId, CurlAndGrowMove, Intents.Defend, Intents.Buff);
        web.FollowUpState = curlAndGrow;
        curlAndGrow.FollowUpState = pounce;
        pounce.FollowUpState = web;
        return new MonsterMoveStateMachineDef(new AiStateDef[] { curlAndGrow, web, pounce }, web);
    }

    private static void CurlAndGrowMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        CreatureCmd.GainBlock(cs, self, CurlBlock(cs), ValueProp.Move, null);
        PowerCmd.Apply<StrengthPower>(cs, self, GrowStrength(cs), self, null);
    }

    private static void PounceMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, PounceDamage(cs)).FromMonster(self).Execute();
    }

    private static void WebMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
        DamageCmd.Attack(cs, WebDamage(cs)).FromMonster(self).Execute();
        PowerCmd.Apply<FrailPower>(cs, targets, WebFrail, self, null);
    }
}
