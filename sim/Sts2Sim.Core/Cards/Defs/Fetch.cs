using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Fetch : CardDef
{
    public const string CardId = "FETCH";
    public const int Cost = 0;
    public const decimal BaseOstyDamage = 3m;
    public const int BaseCards = 1;
    public const decimal OstyDamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public Fetch()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public static bool HasBeenPlayedThisTurn(CombatState cs, CardInstance card)
    {
        var entries = cs.History.CardPlaysFinished;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.CardPlay.IsPlayOf(card) && e.HappenedThisTurn(cs))
            {
                return true;
            }
        }
        return false;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var fetch = play.Card;
        var owner = fetch.Owner;
        if (Osty.CheckMissingWithAnim(cs, owner))
        {
            return;
        }
        DamageCmd.Attack(cs, fetch.Vars.OstyDamage.BaseValue).FromOsty(PlayerPets.GetOsty(cs, owner)!, fetch, play).Targeting(target).Execute();
        if (!HasBeenPlayedThisTurn(cs, fetch))
        {
            CardPileCmd.Draw(cs, fetch.Vars.Cards.BaseValue, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }
}
