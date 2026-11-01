namespace Sts2Sim.Core.Cards;

public sealed class TeslaCoil : CardDef
{
    public const string CardId = "TESLA_COIL";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal DamageUpgrade = 1m;

    public TeslaCoil()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var teslaCoil = play.Card;
        DamageCmd.Attack(cs, teslaCoil.Vars.Damage.BaseValue).FromCard(teslaCoil, play).Targeting(target).Execute();
        var lightningOrbs = teslaCoil.Owner.OrbQueue.Orbs.OfType<LightningOrb>().ToList();
        foreach (var lightningOrb in lightningOrbs)
        {
            OrbCmd.Passive(cs, lightningOrb, play.Target);
            if (teslaCoil.IsUpgraded)
            {
                OrbCmd.Passive(cs, lightningOrb, play.Target);
            }
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
