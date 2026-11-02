namespace Sts2Sim.Core.Cards;

public sealed class Begone : CardDef
{
    public const string CardId = "BEGONE";
    public const int Cost = 1;
    public const int SelectCount = 1;
    public const string MinionStrikeId = "MINION_STRIKE";

    public Begone()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            var minion = cs.CreateCard(MinionStrikeId, owner);
            if (card.IsUpgraded)
            {
                CardCmd.Upgrade(cs, minion);
            }
            CardCmd.Transform(cs, cardModel, minion);
        }
    }
}
