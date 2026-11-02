namespace Sts2Sim.Core.Cards;

public sealed class Thrash : CardDef
{
    public const string CardId = "THRASH";
    public const int Cost = 1;
    public const decimal BaseDamage = 4m;
    public const decimal DamageUpgrade = 2m;
    public const int HitCount = 2;
    public const ushort StateTag = 0xCA33;

    public Thrash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override CardExtraState? CreateExtraState() => new ThrashState();

    public static decimal GetExtraDamage(CardInstance card) => ((ThrashState)card.Extra!).ExtraDamage;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var thrash = play.Card;
        DamageCmd.Attack(cs, thrash.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(thrash, play).Targeting(target).Execute();
        var owner = thrash.Owner;
        var pile = owner.Hand;
        var cardModel = cs.Rng.CombatCardSelection.NextItem(pile.Cards.Where(c => c.Type == CardType.Attack));
        if (cardModel == null)
        {
            return;
        }
        decimal damage = 0m;
        var vars = cardModel.Vars;
        if (vars.ContainsKey(DynamicVarSet.CalculatedDamageName))
        {
            damage = vars.CalculatedDamage.Calculate(cs, cardModel, null);
        }
        else if (vars.ContainsKey(DynamicVarSet.DamageName))
        {
            damage = vars.Damage.BaseValue;
        }
        else if (vars.ContainsKey(DynamicVarSet.OstyDamageName))
        {
            damage = vars.OstyDamage.BaseValue;
        }
        damage = Hook.ModifyDamage(cs.Run, cs, null, owner, damage, ValueProp.Move, cardModel, null, ModifyDamageHookType.All);
        var damage2 = thrash.Vars.Damage;
        damage2.BaseValue += damage;
        ((ThrashState)thrash.Extra!).ExtraDamage += damage;
        CardCmd.Exhaust(cs, cardModel);
    }

    public override void AfterDowngraded(CardInstance card)
    {
        var damage = card.Vars.Damage;
        damage.BaseValue += ((ThrashState)card.Extra!).ExtraDamage;
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private sealed class ThrashState : CardExtraState
    {
        public decimal ExtraDamage { get; set; }

        public override CardExtraState DeepClone() => (ThrashState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteDecimal(ExtraDamage);
        }
    }
}
