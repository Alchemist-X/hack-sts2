namespace Sts2Sim.Core.Cards;

public sealed class CrushUnder : CardDef
{
    public const string CardId = "CRUSH_UNDER";
    public const int Cost = 1;
    public const string CrushUnderPowerId = "CRUSH_UNDER_POWER";
    public const string StrengthLossVarName = "StrengthLoss";
    public const decimal BaseDamage = 8m;
    public const decimal BaseStrengthLoss = 1m;
    public const decimal DamageUpgrade = 1m;
    public const decimal StrengthLossUpgrade = 1m;

    public CrushUnder()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(StrengthLossVarName, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var crushUnder = play.Card;
        var enemies = new List<MonsterState>();
        cs.GetHittableEnemies(enemies);
        DamageCmd.Attack(cs, crushUnder.Vars.Damage.BaseValue).FromCard(crushUnder, play).TargetingAllOpponents().Execute();
        var owner = crushUnder.Owner;
        for (int i = 0; i < enemies.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(CrushUnderPowerId), enemies[i], crushUnder.Vars[StrengthLossVarName].BaseValue, owner, crushUnder);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[StrengthLossVarName].UpgradeValueBy(StrengthLossUpgrade);
    }
}
