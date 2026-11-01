using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Snap : CardDef
{
    public const string CardId = "SNAP";
    public const int Cost = 1;
    public const decimal BaseOstyDamage = 7m;
    public const decimal OstyDamageUpgrade = 3m;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public Snap()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var snap = play.Card;
        var owner = snap.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            DamageCmd.Attack(cs, snap.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, snap, play).Targeting(target).Execute();
        }
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, c => !c.HasKeyword(CardKeyword.Retain), ChoiceSource.For(snap, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.ApplyKeyword(cardModel, CardKeyword.Retain);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }
}
