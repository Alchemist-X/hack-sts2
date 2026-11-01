using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SeekerStrike : CardDef
{
    public const string CardId = "SEEKER_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const int BaseCards = 3;
    public const decimal DamageUpgrade = 3m;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public SeekerStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var seekerStrike = play.Card;
        DamageCmd.Attack(cs, seekerStrike.Vars.Damage.BaseValue).FromCard(seekerStrike, play).Targeting(target).Execute();
        var owner = seekerStrike.Owner;
        var drawPile = owner.DrawPile;
        var shuffled = new List<CardInstance>(drawPile.Cards).StableShuffle(cs.Rng.CombatCardSelection);
        var cardOptions = shuffled.Take(seekerStrike.Vars.Cards.IntValue);
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromCombatPile(cs, drawPile, owner, prefs, c => cardOptions.Contains(c), ChoiceSource.For(seekerStrike, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
