namespace Sts2Sim.Core.Relics;

public sealed class HelicalDart : RelicModel
{
    public const string RelicId = "HELICAL_DART";

    public const string HelicalDartPowerId = "HELICAL_DART_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.DexterityName, 1m)
    };

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.HasOwner && ReferenceEquals(card.Owner, Owner) && card.HasTag(CardTag.Shiv))
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(HelicalDartPowerId), Owner, DynamicVars.Dexterity.IntValue, Owner, null);
        }
    }
}
