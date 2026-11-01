using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BlightStrike : CardDef
{
    public const string CardId = "BLIGHT_STRIKE";
    public const int Cost = 1;
    public const string DoomPowerId = "DOOM_POWER";
    public const decimal BaseDamage = 8m;
    public const decimal DamageUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public BlightStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var blightStrike = play.Card;
        var attackCommand = DamageCmd.Attack(cs, blightStrike.Vars.Damage.BaseValue).FromCard(blightStrike, play).Targeting(target).Execute();
        int totalDamage = 0;
        var results = attackCommand.Results;
        for (int i = 0; i < results.Count; i++)
        {
            totalDamage += results[i].TotalDamage;
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), target, totalDamage, blightStrike.Owner, blightStrike);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
