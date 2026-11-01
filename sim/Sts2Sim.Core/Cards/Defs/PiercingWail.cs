using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class PiercingWail : CardDef
{
    public const string CardId = "PIERCING_WAIL";
    public const int Cost = 1;
    public const string PiercingWailPowerId = "PIERCING_WAIL_POWER";
    public const string StrengthLossKey = "StrengthLoss";
    public const decimal BaseStrengthLoss = 6m;
    public const decimal StrengthLossUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public PiercingWail()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(StrengthLossKey, BaseStrengthLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        for (int i = 0; i < hittable.Count; i++)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(PiercingWailPowerId), hittable[i], card.Vars[StrengthLossKey].BaseValue, owner, card);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[StrengthLossKey].UpgradeValueBy(StrengthLossUpgrade);
    }
}
