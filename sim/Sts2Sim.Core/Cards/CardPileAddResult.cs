namespace Sts2Sim.Core.Cards;

public readonly record struct CardPileAddResult(bool Success, CardInstance CardAdded, PileType? OldPile, PileType TargetPile, IReadOnlyList<AbstractModel>? ModifyingModels);
