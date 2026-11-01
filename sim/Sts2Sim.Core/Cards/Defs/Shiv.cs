using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Shiv : CardDef
{
    public const string CardId = "SHIV";
    public const int Cost = 0;
    public const decimal BaseDamage = 4m;
    public const decimal DamageUpgrade = 2m;
    public const string FanOfKnivesPowerId = "FAN_OF_KNIVES_POWER";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Shiv });

    public Shiv()
        : base(CardId, Cost, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override TargetType GetTargetType(CardInstance card) => HasFanOfKnives(card) ? TargetType.AllEnemies : TargetType.AnyEnemy;

    public static bool HasFanOfKnives(CardInstance card) => card.HasOwner && card.Owner.HasPower(FanOfKnivesPowerId);

    public static CardInstance? CreateInHand(CombatState cs, PlayerState owner, PlayerState? creator = null)
        => CreateInHand(cs, owner, 1, creator).FirstOrDefault();

    public static IReadOnlyList<CardInstance> CreateInHand(CombatState cs, PlayerState owner, int count, PlayerState? creator = null)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(owner);
        if (count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var def = CardRegistry.Get(CardId);
        var shivs = new List<CardInstance>(count);
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(def, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, creator ?? owner);
        return shivs;
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var shiv = play.Card;
        var attackCommand = DamageCmd.Attack(cs, shiv.Vars.Damage.BaseValue).FromCard(shiv, play);
        if (HasFanOfKnives(shiv))
        {
            attackCommand = attackCommand.TargetingAllOpponents();
        }
        else
        {
            var target = play.Target;
            ArgumentNullException.ThrowIfNull(target);
            attackCommand = attackCommand.Targeting(target);
        }
        attackCommand.Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
