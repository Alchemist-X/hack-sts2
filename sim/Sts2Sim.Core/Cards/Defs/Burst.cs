namespace Sts2Sim.Core.Cards;

public sealed class Burst : CardDef
{
    public const string CardId = "BURST";
    public const int Cost = 1;
    public const string BurstPowerId = "BURST_POWER";
    public const string SkillsVarName = "Skills";
    public const decimal BaseSkills = 1m;
    public const decimal SkillsUpgrade = 1m;

    public Burst()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(SkillsVarName, BaseSkills)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var burst = play.Card;
        var owner = burst.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(BurstPowerId), owner, burst.Vars[SkillsVarName].BaseValue, owner, burst);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[SkillsVarName].UpgradeValueBy(SkillsUpgrade);
    }
}
