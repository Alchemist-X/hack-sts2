using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Slimed : CardDef
{
    public const string CardId = "SLIMED";
    public const int Cost = 1;
    public const decimal BaseCards = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Slimed()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar((int)BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var slimed = play.Card;
        CardPileCmd.Draw(cs, slimed.Vars.Cards.BaseValue, slimed.HasOwner ? slimed.Owner : cs.Player);
    }

    public static void AddToCombatAndPreview(CombatState cs, IReadOnlyList<CreatureState> targets, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var def = CardRegistry.Get(CardId);
        for (int t = 0; t < targets.Count; t++)
        {
            var player = PlayerOf(cs, targets[t]);
            if (player.IsDead)
            {
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                var card = cs.CreateCard(def, player);
                CardPileCmd.AddGeneratedCardToCombat(cs, card, pileType, creator, position);
            }
        }
    }

    private static PlayerState PlayerOf(CombatState cs, CreatureState target)
    {
        if (target is PlayerState player)
        {
            return player;
        }
        if (target is MonsterState monster && monster.PetOwnerId != PowerModel.NoCreature && cs.GetCreatureById(monster.PetOwnerId) is PlayerState owner)
        {
            return owner;
        }
        throw new NotSupportedException($"{CardId} can only be added for a player or a pet target (target.Player ?? target.PetOwner); creature {target.CombatId} has neither.");
    }
}
