namespace Sts2Sim.Core.Cards;

public sealed class Slimed : CardDef
{
    public const string CardId = "SLIMED";
    public const int Cost = 1;
    public const decimal BaseCards = 1m;

    private static readonly CardKeyword[] Keywords = { CardKeyword.Exhaust };

    public Slimed()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new(DynamicVarSet.CardsName, BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        CardPileCmd.Draw(cs, play.Card.Vars.Cards.BaseValue, cs.Player);
    }

    public static void AddToCombatAndPreview(CombatState cs, IReadOnlyList<CreatureState> targets, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var def = CardRegistry.Get(CardId);
        for (int t = 0; t < targets.Count; t++)
        {
            var player = targets[t] as PlayerState
                ?? throw new NotSupportedException($"{CardId} can only be added for a player target; pet owners are not modelled (target {targets[t].CombatId}).");
            if (player.IsDead)
            {
                continue;
            }
            for (int i = 0; i < count; i++)
            {
                var card = CardInstance.Create(cs, def);
                CardPileCmd.AddGeneratedCardToCombat(cs, card, pileType, creator, position);
            }
        }
    }
}
