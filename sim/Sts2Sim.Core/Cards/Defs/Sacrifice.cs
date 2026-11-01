using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Sacrifice : CardDef
{
    public const string CardId = "SACRIFICE";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const int OstyMaxHpMultiplier = 3;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OstyMaxHpTimesThree = static (cs, card, _) =>
        CombatStatus.IsOstyAlive(cs, card.Owner) ? CombatStatus.GetOsty(cs, card.Owner)!.MaxHp * OstyMaxHpMultiplier : 0;

    public Sacrifice()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedBlockVar(ValueProp.Move).WithMultiplier(OstyMaxHpTimesThree)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sacrifice = play.Card;
        var owner = sacrifice.Owner;
        if (CombatStatus.IsOstyMissing(cs, owner))
        {
            return;
        }
        var calculatedBlock = sacrifice.Vars.CalculatedBlock;
        decimal blockGain = calculatedBlock.Calculate(cs, sacrifice, play.Target);
        CreatureCmd.Kill(cs, CombatStatus.GetOsty(cs, owner)!);
        CreatureCmd.GainBlock(cs, owner, blockGain, calculatedBlock.Props, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
