namespace Sts2Sim.Core.Run;

public abstract class CharacterDef
{
    public const int DefaultMaxEnergy = 3;

    public abstract string Id { get; }

    public virtual bool IsPlayable => true;

    public abstract string? UnlocksAfterRunAs { get; }

    public abstract int StartingHp { get; }

    public abstract int StartingGold { get; }

    public virtual int MaxEnergy => DefaultMaxEnergy;

    public virtual int BaseOrbSlotCount => 0;

    public abstract string CardPoolId { get; }

    public abstract string RelicPoolId { get; }

    public abstract string PotionPoolId { get; }

    public abstract IReadOnlyList<string> StartingDeck { get; }

    public abstract IReadOnlyList<string> StartingRelics { get; }

    public virtual IReadOnlyList<string> StartingPotions => Array.Empty<string>();

    public abstract int ArchitectAttackVfxCount { get; }

    public RunLoop.CharacterStart ToCharacterStart()
        => new(Id, StartingHp, StartingGold, MaxEnergy, BaseOrbSlotCount, StartingDeck, StartingRelics);
}
