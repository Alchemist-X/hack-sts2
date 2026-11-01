namespace Sts2Sim.Core.Cards;

public sealed class ReaperForm : CardDef
{
    public const string CardId = "REAPER_FORM";
    public const int Cost = 3;
    public const decimal PowerAmount = 1m;
    public const string ReaperFormPowerId = "REAPER_FORM_POWER";

    public ReaperForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, ReaperFormPowerId, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
