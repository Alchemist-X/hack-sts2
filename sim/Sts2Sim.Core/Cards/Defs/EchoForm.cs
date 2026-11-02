using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class EchoForm : CardDef
{
    public const string CardId = "ECHO_FORM";
    public const int Cost = 3;
    public const string EchoFormKey = "EchoForm";
    public const decimal BaseEchoForm = 1m;
    public const string EchoFormPowerId = "ECHO_FORM_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public EchoForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(EchoFormKey, BaseEchoForm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var echoForm = play.Card;
        var owner = echoForm.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(EchoFormPowerId), owner, echoForm.Vars[EchoFormKey].BaseValue, owner, echoForm);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Ethereal);
    }
}
