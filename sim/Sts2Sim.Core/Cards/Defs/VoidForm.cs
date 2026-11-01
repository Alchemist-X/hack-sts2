using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class VoidForm : CardDef
{
    public const string CardId = "VOID_FORM";
    public const int Cost = 3;
    public const decimal BaseVoidForm = 2m;
    public const string VoidFormPowerId = "VOID_FORM_POWER";
    public const string VoidFormPowerVar = "VoidFormPower";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public VoidForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(VoidFormPowerVar, BaseVoidForm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var voidForm = play.Card;
        var owner = voidForm.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(VoidFormPowerId), owner, voidForm.Vars[VoidFormPowerVar].BaseValue, owner, voidForm);
        PlayerCmd.EndTurn(cs, owner, canBackOut: false);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Ethereal);
    }
}
