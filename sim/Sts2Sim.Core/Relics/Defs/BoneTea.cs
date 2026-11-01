namespace Sts2Sim.Core.Relics;

public sealed class BoneTea : RelicModel
{
    public const string RelicId = "BONE_TEA";

    public const string CombatsKey = "Combats";

    private int _combatsLeft = 1;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool IsUsedUp => CombatsLeft <= 0;

    public override bool ShowCounter => false;

    public override int DisplayAmount => Math.Max(0, CombatsLeft);

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CombatsKey, CombatsLeft)
    };

    public int CombatsLeft
    {
        get => _combatsLeft;
        set
        {
            _combatsLeft = value;
            DynamicVars[CombatsKey].BaseValue = _combatsLeft;
            if (IsUsedUp)
            {
                Status = RelicStatus.Disabled;
            }
        }
    }

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (IsUsedUp)
        {
            return;
        }
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }
        if (Owner.TurnNumber > 1)
        {
            return;
        }
        foreach (var card in Owner.Hand.Cards.ToList())
        {
            CardCmd.Upgrade(cs, card);
        }
        CombatsLeft--;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatsLeft);
    }
}
