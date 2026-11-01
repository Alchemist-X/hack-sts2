namespace Sts2Sim.Core.Relics;

public sealed class Byrdpip : RelicModel
{
    public const string RelicId = "BYRDPIP";

    public const string PetMonsterId = "BYRDPIP";

    public const string ByrdonisEggCardId = "BYRDONIS_EGG";

    public const string ByrdSwoopCardId = "BYRD_SWOOP";

    public override string Id => RelicId;

    public override bool AddsPet => true;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool HasUponPickupEffect => true;

    public override bool SpawnsPets => true;

    public override void AfterObtained(RunState rs)
    {
        var cs = rs.Combat;
        bool inProgress = cs != null && cs.IsInProgress;
        var eggs = Owner.Deck.Where(c => c.Id == ByrdonisEggCardId).ToList();
        if (inProgress)
        {
            eggs.AddRange(Owner.AllPiles.SelectMany(p => p.Cards).Where(c => c.Id == ByrdonisEggCardId));
        }
        foreach (var egg in eggs)
        {
            if (cs == null)
            {
                CardCmd.TransformTo(rs, egg, ByrdSwoopCardId);
                continue;
            }
            CardCmd.TransformTo(cs, egg, ByrdSwoopCardId);
        }
        if (inProgress)
        {
            SummonPet(cs!);
        }
    }

    public override void BeforeCombatStart(RunState rs)
    {
        SummonPet(rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart without combat"));
    }

    private void SummonPet(CombatState cs)
    {
        if (!MonsterRegistry.TryGet(PetMonsterId, out var def))
        {
            return;
        }
        PlayerCmd.AddPet(cs, Owner, def);
    }
}
