namespace Sts2Sim.Core.Cards;

public sealed class Monologue : CardDef
{
    public const string CardId = "MONOLOGUE";
    public const int Cost = 0;
    public const string PowerKey = "Power";
    public const decimal BasePower = 1m;
    public const decimal PowerAmount = 1m;
    public const string MonologuePowerId = "MONOLOGUE_POWER";

    public Monologue()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerKey, BasePower)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var monologue = play.Card;
        var owner = monologue.Owner;
        ApplyPower(cs, MonologuePowerId, owner, PowerAmount, owner, monologue);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }

    private static PowerModel? ApplyPower(CombatState cs, string powerId, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return null;
        }
        if (!CombatStatus.CanReceivePowers(cs, target))
        {
            return null;
        }
        var canonical = PowerRegistry.Create(powerId);
        var power = PowerCmd.FindExistingInstanceForStacking(canonical, target, applier);
        if (power == null)
        {
            power = canonical;
            PowerCmd.Apply(cs, power, target, amount, applier, cardSource);
        }
        else if (PowerCmd.ModifyAmount(cs, power, amount, applier, cardSource) == 0)
        {
            power = null;
        }
        return power;
    }
}
