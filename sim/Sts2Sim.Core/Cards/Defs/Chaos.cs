namespace Sts2Sim.Core.Cards;

public sealed class Chaos : CardDef
{
    public const string CardId = "CHAOS";
    public const int Cost = 1;
    public const int BaseRepeat = 1;
    public const decimal RepeatUpgrade = 1m;

    public Chaos()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var chaos = play.Card;
        var owner = chaos.Owner;
        for (int i = 0; i < chaos.Vars.Repeat.IntValue; i++)
        {
            OrbCmd.Channel(cs, OrbModel.GetRandomOrb(cs.Run.Rng.CombatOrbGeneration), owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
